using System;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;

public class ToolGravitygun : Tool
{
	[SerializeField]
	private float moveStrength = 20.0f;

	// NEW: Maximum force the gun can exert (ideal for future upgrades)
	[SerializeField]
	private float maxPullForce = 500.0f;

	// NEW: Maximum torque the gun can exert to hold rotation
	[SerializeField]
	private float maxTorque = 200.0f;

	[SerializeField]
	private float minPushforce = 100.0f;

	[SerializeField]
	private float maxPushforce = 200.0f;

	[SerializeField]
	private float timeToMaxCharge = 2.5f;

	[SerializeField]
	private float maxPushDistance = 10.0f;

	[SerializeField]
	private float maxGrabDistance = 10.0f;

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

	private bool isHolding;
	private bool isCharging;
	private float currentCharge;
	private Rigidbody grabbedRigidbody;
	private Transform playerCamera;

	private Vector3 localGrabOffset;
	private Quaternion initialGrabRotation;

	public float CurrentCharge01 => Mathf.Clamp01(currentCharge / timeToMaxCharge);
	public event Action<float> OnShootEvent = delegate { };

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
		GameCursor.SetCursorDefault();
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

		float PushForce = ((maxPushforce - minPushforce) * CurrentCharge01) + minPushforce;

		RaycastHit[] hits = Physics.SphereCastAll(playerCamera.position, pushSpherecastRadius, playerCamera.forward, maxPushDistance);

		foreach (RaycastHit hit in hits)
		{
			if (hit.rigidbody != null && hit.transform.gameObject != this.NetworkObject.transform.gameObject)
			{
				Vector3 EvaluatedPushForce = playerCamera.forward * PushForce * pushFalloff.Evaluate(NormalizeAndClamp(hit.distance, 0.0f, maxPushDistance));

				if (hit.transform.TryGetComponent<NetworkObject>(out NetworkObject targetObjects))
					ApplyPushForceServerRpc(targetObjects.NetworkObjectId, EvaluatedPushForce, hit.point);
			}

			if (hit.transform.TryGetComponent(out InteractableView interactableView))
				interactableView.ShowEffectForTime(2.0f);
		}

		PlaySecondaryShootEffectEveryoneRpc();
		currentCharge = 0;
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
	}

	private void FixedUpdate()
	{
		if (!IsOwner) return;

		ShowInteractable();
		MoveHeldInteractable();
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

	private void MoveHeldInteractable()
	{
		if (!isHolding) return;
		if (!grabbedRigidbody.TryGetComponent(out NetworkObject targetObject)) return;

		Vector3 worldGrabPoint = grabbedRigidbody.transform.TransformPoint(localGrabOffset);
		Vector3 ToTarget = grabPoint.position - worldGrabPoint;
		float Distance = ToTarget.magnitude;

		Vector3 Direction = ToTarget.normalized;
		float Speed = maxSpeed;
		if (Distance < slowingRadius)
		{
			float t = Distance / slowingRadius;
			Speed = maxSpeed * t;
		}

		Vector3 DesiredVelocity = Direction * Speed;
		Vector3 pointVelocity = grabbedRigidbody.GetPointVelocity(worldGrabPoint);

		Vector3 Steering = DesiredVelocity - pointVelocity;
		Steering = Vector3.ClampMagnitude(Steering, moveStrength);

		// NEW: Calculate required force based on mass, then clamp to maxPullForce upgrade threshold
		Vector3 requiredForce = Steering * grabbedRigidbody.mass;
		Vector3 linearForce = Vector3.ClampMagnitude(requiredForce, maxPullForce);

		// Rotational alignment
		Quaternion deltaRot = initialGrabRotation * Quaternion.Inverse(grabbedRigidbody.rotation);
		deltaRot.ToAngleAxis(out float angle, out Vector3 axis);

		if (angle > 180f) angle -= 360f;

		if (angle == 0 || float.IsNaN(axis.x) || float.IsInfinity(axis.x))
		{
			axis = Vector3.zero;
			angle = 0;
		}

		Vector3 angularTarget = axis.normalized * (angle * Mathf.Deg2Rad);
		Vector3 requiredTorque = (angularTarget * rotationSpring) - (grabbedRigidbody.angularVelocity * rotationDamper);
		requiredTorque *= grabbedRigidbody.mass;

		// NEW: Clamp torque to maxTorque capacity
		Vector3 torque = Vector3.ClampMagnitude(requiredTorque, maxTorque);

		ApplyGrabForceServerRpc(targetObject.NetworkObjectId, linearForce, worldGrabPoint, torque);
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

	[Rpc(SendTo.Server)]
	private void ApplyGrabForceServerRpc(ulong targetNetworkObject, Vector3 linearForce, Vector3 forcePosition, Vector3 torque)
	{
		if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObject, out NetworkObject targetObject))
		{
			if (targetObject.TryGetComponent<Rigidbody>(out Rigidbody targetRigidbody))
			{
				targetRigidbody.AddForceAtPosition(linearForce, forcePosition, ForceMode.Force);
				targetRigidbody.AddTorque(torque, ForceMode.Force);
			}
		}
	}

	[Rpc(SendTo.Server)]
	private void SetGrabbedObjectVelocityServerRpc(ulong targetNetworkObject, Vector3 velocity)
	{
		if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObject, out NetworkObject targetObject))
		{
			if (targetObject.TryGetComponent<Rigidbody>(out Rigidbody targetRigidbody))
			{
				targetRigidbody.linearVelocity = velocity;
			}
		}
	}
}