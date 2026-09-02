using System;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;

public class ToolGravitygun : Tool
{
	[SerializeField]
	private PredictedPlayerController playerController;

	[SerializeField]
	private float moveStrength = 20.0f;

	// NEW: Maximum force the gun can exert (ideal for future upgrades)
	[SerializeField]
	private float maxPullMass = 100.0f;

	// NEW: Maximum torque the gun can exert to hold rotation
	[SerializeField]
	private float maxTorque = 200.0f;

	[SerializeField]
	private float minPushforce = 100.0f;

	[SerializeField]
	private float maxPushforce = 200.0f;

	// NEW: Separate push forces for balancing player knockback
	[SerializeField]
	private float minPlayerPushforce = 200.0f;

	[SerializeField]
	private float maxPlayerPushforce = 2000.0f;

	[SerializeField]
	private float timeToMaxCharge = 2.5f;

	[SerializeField]
	private float maxPushDistance = 10.0f;

	[SerializeField]
	private float maxGrabDistance = 10.0f;

	// NEW: The maximum distance the object can get from the player before breaking the hold
	[SerializeField]
	private float maxHoldDistance = 15.0f;

	[SerializeField]
	private float pushSpherecastRadius = 1.0f;

	[SerializeField]
	private float grabSpherecastRadius = 0.25f;

	[SerializeField]
	private AnimationCurve pushFalloff = new();

	[SerializeField]
	private float arrivalThreshold = 0.1f;

	[SerializeField]
	private float slowingRadius = 3.0f;

	[SerializeField]
	private float maxSpeed = 20.0f;

	[SerializeField]
	private Transform grabPoint;

	// ADJUSTED: Lowered values for a looser, more wobbly feel
	[SerializeField]
	private float rotationSpring = 15.0f;

	[SerializeField]
	private float rotationDamper = 2.0f;

	// NEW: Speed at which the object is reeled in
	[SerializeField]
	private float pullSpeed = 10.0f;

	// NEW: The closest the grab point can be pulled to the player
	[SerializeField]
	private float minPullDistance = 1.5f;

	[SerializeField]
	private Rigidbody playerRigidbody;

	private bool isPulling;

	private bool isHolding;
	private bool isCharging;
	private float currentCharge;
	private Rigidbody grabbedRigidbody;
	private Transform playerCamera;

	private Vector3 localGrabOffset;
	private Quaternion initialGrabRotation;

	public float CurrentCharge01 => Mathf.Clamp01(currentCharge / timeToMaxCharge);
	public event Action<float> OnShootEvent = delegate { };


	// NEW: Sync the reel-in distance without RPC spam
	private NetworkVariable<float> networkedGrabDistance = new NetworkVariable<float>(10.0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

	// NEW: Server-side tracking variables
	private bool serverIsHolding;
	private Rigidbody serverGrabbedRigidbody;
	private Vector3 serverLocalGrabOffset;
	private Quaternion serverInitialGrabRotation;

	protected override void OnNetworkPostSpawn()
	{
		if (!IsOwner) return;

		playerCamera = transform.parent.parent;
	}

	public override void PressPrimary()
	{
		if (!IsOwner) return;

		if (TryGetFirstHitInteractable(out Interactable interactable, out Rigidbody rigidbody, out Collider collider, out Vector3 hitPoint))
		{
			if (collider.TryGetComponent(out SpaceshipPart spaceshipPart))
			{
				spaceshipPart.SeverPartFromAll();
				if (TryGetFirstHitInteractable(out interactable, out rigidbody, out collider, out hitPoint))
					StartHolding(rigidbody, interactable, hitPoint);
			}
			else
			{
				StartHolding(rigidbody, interactable, hitPoint);
			}
		}
	}

	private void StartHolding(Rigidbody rigidbody, Interactable interactable, Vector3 hitPoint)
	{
		grabbedRigidbody = rigidbody;
		localGrabOffset = grabbedRigidbody.transform.InverseTransformPoint(hitPoint);
		initialGrabRotation = grabbedRigidbody.rotation;

		// Initialize the grab distance and sync it
		float initialDistance = Vector3.Distance(playerCamera.position, hitPoint);
		grabPoint.localPosition = new Vector3(grabPoint.localPosition.x, grabPoint.localPosition.y, initialDistance);
		networkedGrabDistance.Value = initialDistance;

		// NEW: Tell the server to start tracking this object
		if (rigidbody.TryGetComponent(out NetworkObject netObj))
		{
			StartHoldingServerRpc(netObj.NetworkObjectId, localGrabOffset, initialGrabRotation);
		}

		interactable.SetIsBeingHeldServerRpc(true);
		isHolding = true;
		GameCursor.SetCursorIsInteracting();
	}

	public override void ReleasePrimary()
	{
		if (!IsOwner) return;
		if (!isHolding) return;

		if (grabbedRigidbody.TryGetComponent(out Interactable interactable))
			interactable.SetIsBeingHeldServerRpc(false);

		isHolding = false;
		grabbedRigidbody = null;
		isPulling = false;

		if (playerController != null)
		{
			playerController.SetContinuousForce(Vector3.zero);
		}

		// NEW: Tell the server to stop tracking
		StopHoldingServerRpc();

		GameCursor.SetCursorDefault();
	}

	[Rpc(SendTo.Server)]
	private void StartHoldingServerRpc(ulong targetNetworkObject, Vector3 localOffset, Quaternion initialRot)
	{
		if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObject, out NetworkObject targetObject))
		{
			serverGrabbedRigidbody = targetObject.GetComponent<Rigidbody>();
			serverLocalGrabOffset = localOffset;
			serverInitialGrabRotation = initialRot;
			serverIsHolding = true;
		}
	}

	[Rpc(SendTo.Server)]
	private void StopHoldingServerRpc()
	{
		serverIsHolding = false;
		serverGrabbedRigidbody = null;
	}

	public override void PressSecondary()
	{
		if (!IsOwner) return;

		isCharging = true;
		currentCharge = 0.0f;
	}

	public override void ReleaseSecondary()
	{
		if (!IsOwner) return;

		isCharging = false;

		// NEW: Calculate both force types based on current charge
		float ObjectPushForce = ((maxPushforce - minPushforce) * CurrentCharge01) + minPushforce;
		float PlayerPushForce = ((maxPlayerPushforce - minPlayerPushforce) * CurrentCharge01) + minPlayerPushforce;

		if (isHolding)
		{
			Rigidbody heldRb = grabbedRigidbody;

			bool isStatic = false;
			if (heldRb.TryGetComponent(out Interactable interactable))
			{
				isStatic = interactable.GetIsStatic();
			}

			ReleasePrimary();

			if (isStatic)
			{
				if (playerController != null)
				{
					// NEW: Apply the Player-specific push force when hookshotting backwards
					playerController.ApplyImpulse(-playerCamera.forward * PlayerPushForce);
				}
			}
			else
			{
				// (Optional safety) Check if the held object is somehow another player
				float forceToUse = heldRb.TryGetComponent<PredictedPlayerController>(out _) ? PlayerPushForce : ObjectPushForce;

				if (heldRb.TryGetComponent(out NetworkObject targetNetObj))
				{
					ApplyPushForceServerRpc(targetNetObj.NetworkObjectId, playerCamera.forward * forceToUse, heldRb.position);
				}
			}
		}
		else
		{
			RaycastHit[] hits = Physics.SphereCastAll(playerCamera.position, pushSpherecastRadius, playerCamera.forward, maxPushDistance);

			foreach (RaycastHit hit in hits)
			{
				if (hit.rigidbody != null && hit.transform.gameObject != this.NetworkObject.transform.gameObject)
				{
					// NEW: Check if the raycast hit a player or an object, and assign the proper force
					float forceToUse = hit.transform.TryGetComponent<PredictedPlayerController>(out _) ? PlayerPushForce : ObjectPushForce;

					Vector3 EvaluatedPushForce = playerCamera.forward * forceToUse * pushFalloff.Evaluate(NormalizeAndClamp(hit.distance, 0.0f, maxPushDistance));

					if (hit.transform.TryGetComponent<NetworkObject>(out NetworkObject targetObjects))
						ApplyPushForceServerRpc(targetObjects.NetworkObjectId, EvaluatedPushForce, hit.point);
				}

				if (hit.transform.TryGetComponent(out InteractableView interactableView))
					interactableView.ShowEffectForTime(2.0f);
			}
		}

		PlaySecondaryShootEffectEveryoneRpc();
		currentCharge = 0;
	}

	public override void PressTertiary()
	{
		if (!IsOwner) return;
		isPulling = true;
	}

	public override void ReleaseTertiary()
	{
		if (!IsOwner) return;
		isPulling = false;
	}

	private bool TryGetFirstHitInteractable(out Interactable interactable, out Rigidbody rigidbody, out Collider collider, out Vector3 hitPoint)
	{
		interactable = null;
		rigidbody = null;
		collider = null;
		hitPoint = Vector3.zero;
		bool hitInteractable = false;

		RaycastHit[] hits = Physics.SphereCastAll(playerCamera.position, grabSpherecastRadius, playerCamera.forward, maxGrabDistance);
		Array.Sort(hits, delegate (RaycastHit x, RaycastHit y) { return x.distance.CompareTo(y.distance); });
		foreach (RaycastHit hit in hits)
		{
			if (hit.rigidbody != null && hit.transform.gameObject != this.NetworkObject.transform.gameObject)
			{
				if (!hit.collider.TryGetComponent(out Interactable rbInteractable))
					continue;

				interactable = rbInteractable;
				rigidbody = hit.rigidbody;
				collider = hit.collider;
				hitPoint = hit.point;
				hitInteractable = true;
				break;
			}
		}

		return hitInteractable;
	}

	private void Update()
	{
		if (!IsOwner) return;

		if (isCharging)
		{
			currentCharge += Time.deltaTime;
		}

		if (isHolding && isPulling && grabbedRigidbody != null)
		{
			bool isStatic = false;
			if (grabbedRigidbody.TryGetComponent(out Interactable interactable))
			{
				isStatic = interactable.GetIsStatic();
			}

			float newZ;

			if (!isStatic)
			{
				float currentZ = grabPoint.localPosition.z;
				newZ = Mathf.Max(minPullDistance, currentZ - (pullSpeed * Time.deltaTime));
			}
			else
			{
				Vector3 worldGrabPoint = grabbedRigidbody.transform.TransformPoint(localGrabOffset);
				float actualDistance = Vector3.Distance(playerCamera.position, worldGrabPoint);
				newZ = Mathf.Max(minPullDistance, actualDistance);
			}

			// Apply locally and sync to server
			grabPoint.localPosition = new Vector3(grabPoint.localPosition.x, grabPoint.localPosition.y, newZ);
			networkedGrabDistance.Value = newZ;
		}
	}

	private void FixedUpdate()
	{
		// Client handles their own player grappling
		if (IsOwner)
		{
			ShowInteractable();
			ClientHandleGrapple();
		}

		// Server handles the actual object physics
		if (IsServer)
		{
			ServerHandleObjectPull();
		}
	}

	private void ClientHandleGrapple()
	{
		if (!isHolding || grabbedRigidbody == null) return;

		Vector3 worldGrabPoint = grabbedRigidbody.transform.TransformPoint(localGrabOffset);

		float distanceToPlayer = Vector3.Distance(playerCamera.position, worldGrabPoint);
		if (distanceToPlayer > maxHoldDistance)
		{
			ReleasePrimary();
			return;
		}

		bool isStatic = false;
		if (grabbedRigidbody.TryGetComponent(out Interactable interactable))
		{
			isStatic = interactable.GetIsStatic();
		}

		bool isHeavy = grabbedRigidbody.mass > maxPullMass;
		Vector3 appliedPlayerForce = Vector3.zero;

		// === PULL THE PLAYER === 
		if ((isStatic || isHeavy) && isPulling && playerController != null)
		{
			Vector3 playerToTarget = worldGrabPoint - playerCamera.position;
			float playerDistance = playerToTarget.magnitude;

			if (playerDistance > minPullDistance)
			{
				Vector3 playerDirection = playerToTarget.normalized;
				Vector3 playerDesiredVelocity = playerDirection * pullSpeed;
				Vector3 playerVelocity = playerController.LinearVelocity;

				Vector3 playerSteering = playerDesiredVelocity - playerVelocity;
				Vector3 playerLinearForce = Vector3.ClampMagnitude(playerSteering, moveStrength);

				playerLinearForce *= playerController.Mass;
				appliedPlayerForce = playerLinearForce;
			}
		}

		if (playerController != null)
		{
			playerController.SetContinuousForce(appliedPlayerForce);
		}
	}

	private void ServerHandleObjectPull()
	{
		if (!serverIsHolding || serverGrabbedRigidbody == null) return;

		// Sync the server's grab point to match the client's reel-in distance
		grabPoint.localPosition = new Vector3(grabPoint.localPosition.x, grabPoint.localPosition.y, networkedGrabDistance.Value);

		Vector3 worldGrabPoint = serverGrabbedRigidbody.transform.TransformPoint(serverLocalGrabOffset);

		bool isStatic = false;
		if (serverGrabbedRigidbody.TryGetComponent(out Interactable interactable))
		{
			isStatic = interactable.GetIsStatic();
		}

		// === PULL THE OBJECT === 
		if (!isStatic)
		{
			Vector3 ToTarget = grabPoint.position - worldGrabPoint;
			float Distance = ToTarget.magnitude;

			float weightRatio = Mathf.Clamp01(maxPullMass / serverGrabbedRigidbody.mass);
			float currentMaxSpeed = maxSpeed * weightRatio;

			Vector3 Direction = ToTarget.normalized;
			float Speed = currentMaxSpeed;

			if (Distance < slowingRadius)
			{
				float t = Distance / slowingRadius;
				Speed = currentMaxSpeed * t;
			}

			Vector3 DesiredVelocity = Direction * Speed;
			Vector3 pointVelocity = serverGrabbedRigidbody.GetPointVelocity(worldGrabPoint);

			Vector3 Steering = DesiredVelocity - pointVelocity;
			Vector3 linearForce = Vector3.ClampMagnitude(Steering, moveStrength);

			Quaternion deltaRot = serverInitialGrabRotation * Quaternion.Inverse(serverGrabbedRigidbody.rotation);
			deltaRot.ToAngleAxis(out float angle, out Vector3 axis);

			if (angle > 180f) angle -= 360f;

			if (angle == 0 || float.IsNaN(axis.x) || float.IsInfinity(axis.x))
			{
				axis = Vector3.zero;
				angle = 0;
			}

			Vector3 angularTarget = axis.normalized * (angle * Mathf.Deg2Rad);
			Vector3 requiredTorque = (angularTarget * rotationSpring) - (serverGrabbedRigidbody.angularVelocity * rotationDamper);
			Vector3 torque = Vector3.ClampMagnitude(requiredTorque, maxTorque);

			float massMultiplier = Mathf.Min(serverGrabbedRigidbody.mass, maxPullMass);
			linearForce *= massMultiplier;
			torque *= massMultiplier;

			// Server applies forces directly. No more RPC spam!
			serverGrabbedRigidbody.AddForceAtPosition(linearForce, worldGrabPoint, ForceMode.Force);
			serverGrabbedRigidbody.AddTorque(torque, ForceMode.Force);
		}
	}

	private void ShowInteractable()
	{
		if (isHolding || isCharging)
			return;

		bool shouldShowInteractableCursor = TryGetFirstHitInteractable(out _, out _, out _, out _);
		if (shouldShowInteractableCursor)
			GameCursor.SetCursorCanInteract();
		else
			GameCursor.SetCursorDefault();
	}

	public static float NormalizeAndClamp(float value, float min, float max)
	{
		if (max == min) return 0f;

		float normalized = (value - min) / (max - min);
		return Mathf.Clamp(normalized, 0f, 1f);
	}

	[Rpc(SendTo.Everyone)]
	private void PlaySecondaryShootEffectEveryoneRpc()
	{
		OnShootEvent.Invoke(CurrentCharge01);
	}

	[Rpc(SendTo.Server)]
	private void ApplyPushForceServerRpc(ulong targetNetworkObject, Vector3 linearForce, Vector3 forcePosition)
	{
		if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObject, out NetworkObject targetObject))
		{
			if (targetObject.TryGetComponent<Rigidbody>(out Rigidbody targetRigidbody))
			{
				targetRigidbody.AddForceAtPosition(linearForce, forcePosition, ForceMode.Impulse);
			}
		}
	}
}