using System;
using System.Collections.Generic;
using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using Unity.Netcode;
using UnityEngine;

public class ToolGravitygun : Tool
{
	[SerializeField]
	private PredictedPlayerController playerController;

	[SerializeField]
	private float moveStrength = 20.0f;

	[Tooltip("The maximum mass the gravitygun can move efficiantly")]
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
	[SerializeField] 
	private List<LineRenderer> _lineRenderers;
	[SerializeField] 
	private float _lineTweenDuration = 0.5f;
	[SerializeField] 
	private float _normalRaycastDistance = 0.1f;
	[SerializeField] 
	private float _grabNormalOffset = 1.0f;

	private bool isPulling;
	private float _lineTween01 = 0;

	private bool isHolding;
	private bool isCharging;
	private float currentCharge;
	private Rigidbody grabbedRigidbody;
	private Interactable grabbedInteractable;
	private RaycastHit grabbedHit;
	private Transform playerCamera;

	private Vector3 localGrabOffset;
	private Vector3 localGrabNormal;
	private Quaternion initialGrabRotation;

	private Vector3 _grabbedLocalPoint;
	private List<float> _lineRendererStartWidths = new();
	private TweenerCore<float, float, FloatOptions> _lineTween;

	public float CurrentCharge01 => Mathf.Clamp01(currentCharge / timeToMaxCharge);
	public bool IsHolding => isHolding;
	public Rigidbody GrabbedRigidbody => grabbedRigidbody;
	public RaycastHit GrabbedHit => grabbedHit;
	public Vector3 LocalGrabOffset => localGrabOffset;
	public Vector3 LocalGrabNormal => localGrabNormal;
	public event Action<float> OnShootEvent = delegate { };
	public event Action<Interactable> OnStartHoldingEvent = delegate { };
	public event Action<Interactable> OnStopHoldingEvent = delegate { };


	// NEW: Sync the reel-in distance without RPC spam
	private NetworkVariable<float> networkedGrabDistance = new NetworkVariable<float>(10.0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

	// NEW: Server-side tracking variables
	private bool serverIsHolding;
	private Rigidbody serverGrabbedRigidbody;
	private Vector3 serverLocalGrabOffset;
	private Quaternion serverInitialGrabRotation;

	private void Awake()
	{
		_lineRendererStartWidths.Clear();
		for (int i = 0; i < _lineRenderers.Count; i++)
		{
			_lineRendererStartWidths.Add(_lineRenderers[i].widthMultiplier);
		}
	}

	protected override void OnNetworkPostSpawn()
	{
		if (!IsOwner) return;

		playerCamera = transform.parent.parent;
	}

	public override void PressPrimary()
	{
		if (!IsOwner) return;

		if (TryGetFirstHitInteractable(out Interactable interactable, out Rigidbody rigidbody, out Collider collider, out Vector3 hitPoint, out RaycastHit hit))
		{
			if (collider.TryGetComponent(out SpaceshipPart spaceshipPart))
			{
				//spaceshipPart.SeverPartFromAll();
				if (TryGetFirstHitInteractable(out interactable, out rigidbody, out collider, out hitPoint, out hit))
				{
					StartHolding(rigidbody, interactable, hitPoint, hit);
				}
			}
			else
			{
				StartHolding(rigidbody, interactable, hitPoint, hit);
			}
		}
	}

	private void StartHolding(Rigidbody rigidbody, Interactable interactable, Vector3 hitPoint, RaycastHit hit)
	{
		grabbedHit = hit;
		grabbedRigidbody = rigidbody;
		localGrabOffset = grabbedRigidbody.transform.InverseTransformPoint(hitPoint);
		localGrabNormal = grabbedRigidbody.transform.InverseTransformDirection(GetAverageLookAtNormal());
		initialGrabRotation = grabbedRigidbody.rotation;
		grabbedInteractable = interactable;

		// Initialize the grab distance and sync it
		float initialDistance = Vector3.Distance(playerCamera.position, hitPoint);
		grabPoint.localPosition = new Vector3(grabPoint.localPosition.x, grabPoint.localPosition.y, initialDistance);
		networkedGrabDistance.Value = initialDistance;

		// NEW: Tell the server to start tracking this object
		if (rigidbody.TryGetComponent(out NetworkObject netObj))
		{
			StartHoldingServerRpc(netObj.NetworkObjectId, localGrabOffset, initialGrabRotation);
		}

		grabbedInteractable.SetIsBeingHeldServerRpc(true);
		isHolding = true;

		_grabbedLocalPoint = rigidbody.transform.InverseTransformPoint(hitPoint);

		_lineTween.Kill();
		_lineTween = DOTween.To(() => _lineTween01, LineTweenSetter, 1, _lineTweenDuration);
		GameCursor.SetCursorIsInteracting();
		OnStartHoldingEvent.Invoke(interactable);
	}

	private void LineTweenSetter(float x)
	{
		_lineTween01 = x;
		for (int i = 0; i < _lineRenderers.Count; i++)
		{
			_lineRenderers[i].widthMultiplier = _lineTween01 * _lineRendererStartWidths[i];
		}
	}

	public override void ReleasePrimary()
	{
		if (!isHolding) return;
		if (!IsOwner) return;
		
		OnStopHoldingEvent.Invoke(grabbedInteractable);
		
		grabbedInteractable.SetIsBeingHeldServerRpc(false);

		isHolding = false;
		grabbedRigidbody = null;
		isPulling = false;
		grabbedInteractable = null;

		if (playerController != null)
		{
			playerController.SetContinuousForce(Vector3.zero);
		}

		// NEW: Tell the server to stop tracking
		StopHoldingServerRpc();

		_lineTween.Kill();
		_lineTween = DOTween.To(() => _lineTween01, LineTweenSetter, 0, _lineTweenDuration);
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

	private bool TryGetFirstHitInteractable(out Interactable interactable, out Rigidbody rigidbody, out Collider collider, out Vector3 hitPoint, out RaycastHit outHit)
	{
		interactable = null;
		rigidbody = null;
		collider = null;
		outHit = default;
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
				outHit = hit;
				break;
			}
		}

		return hitInteractable;
	}

	private Vector3 GetAverageLookAtNormal()
	{
		int hitsCount = 0;
		Vector3 totalNormal = Vector3.zero;

		RaycastHit hit;
		if (Physics.SphereCast(playerCamera.position, grabSpherecastRadius * .5f, playerCamera.forward, out hit, maxGrabDistance * 1.5f))
			AddHit(hit);
		if (Physics.SphereCast(playerCamera.position + playerCamera.up * _normalRaycastDistance, grabSpherecastRadius * .5f, playerCamera.forward, out hit, maxGrabDistance * 1.5f))
			AddHit(hit);
		if (Physics.SphereCast(playerCamera.position - playerCamera.up * _normalRaycastDistance, grabSpherecastRadius * .5f, playerCamera.forward, out hit, maxGrabDistance * 1.5f))
			AddHit(hit);
		if (Physics.SphereCast(playerCamera.position + playerCamera.right * _normalRaycastDistance, grabSpherecastRadius * .5f, playerCamera.forward, out hit, maxGrabDistance * 1.5f))
			AddHit(hit);
		if (Physics.SphereCast(playerCamera.position - playerCamera.right * _normalRaycastDistance, grabSpherecastRadius * .5f, playerCamera.forward, out hit, maxGrabDistance * 1.5f))
			AddHit(hit);

		void AddHit(RaycastHit hit)
		{
			hitsCount++;
			totalNormal += hit.normal;
		}

		return totalNormal / hitsCount;
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
		
		UpdateLine();
	}

	private void UpdateLine()
	{
		foreach (LineRenderer lineRenderer in _lineRenderers)
		{
			lineRenderer.enabled = isHolding;
		}
		
		if (!isHolding) return;

		Vector3 p0 = _lineRenderers[0].transform.position;
		Vector3 p1 = grabPoint.position;
		Vector3 p3 = grabbedRigidbody.transform.TransformPoint(_grabbedLocalPoint);
		Vector3 p2 = p3 + grabbedRigidbody.transform.TransformDirection(localGrabNormal) * _grabNormalOffset; 

		int positionCount = _lineRenderers[0].positionCount;
		for (int i = 0; i < positionCount; i++)
		{
			Vector3 point = Sample((float)i / (positionCount - 1));
			Vector3 localPosition = _lineRenderers[0].transform.InverseTransformPoint(point);
			SetPosition(i, localPosition * _lineTween01);
		}

		Vector3 Sample(float perc01)
		{
			Vector3 lerpAB = Vector3.Lerp(p0, p1, perc01);
			Vector3 lerpBC = Vector3.Lerp(p1, p2, perc01);
			Vector3 lerpCD = Vector3.Lerp(p2, p3, perc01);

			Vector3 startLerp = Vector3.Lerp(lerpAB, lerpBC, perc01);
			Vector3 endLerp = Vector3.Lerp(lerpBC, lerpCD, perc01);
			
			return Vector3.Lerp(startLerp, endLerp, perc01);
		}
	}

	private void SetPosition(int index, Vector3 pos)
	{
		foreach (LineRenderer lineRenderer in _lineRenderers)
		{
			lineRenderer.SetPosition(index, pos);
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

		bool shouldShowInteractableCursor = TryGetFirstHitInteractable(out _, out _, out _, out _, out _);
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