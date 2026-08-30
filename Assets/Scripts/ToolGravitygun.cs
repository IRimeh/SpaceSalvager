using System;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;

public class ToolGravitygun : Tool
{
	[SerializeField]
	private float moveStrength = 20.0f;

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

	private bool isHolding;
	private bool isCharging;
	private float currentCharge;
	private Rigidbody grabbedRigidbody;
	private Transform playerCamera;

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

		RaycastHit[] hits = Physics.SphereCastAll(playerCamera.position, grabSpherecastRadius, playerCamera.forward, maxGrabDistance);

		Array.Sort(hits, delegate (RaycastHit x, RaycastHit y) { return x.distance.CompareTo(y.distance); });

		foreach (RaycastHit hit in hits)
		{
			if (hit.rigidbody != null && hit.transform.gameObject != this.NetworkObject.transform.gameObject)
			{
				grabbedRigidbody = hit.rigidbody;
				isHolding = true;
				if (grabbedRigidbody.TryGetComponent<Interactable>(out Interactable interactable)) {
					interactable.SetIsBeingHeldServerRpc(true);
				}
				break;
			}
		}
	}

	public override void ReleasePrimary()
	{
		if (!IsOwner) return;

		if (isHolding) {
			if (grabbedRigidbody.TryGetComponent<Interactable>(out Interactable interactable))
			{
				interactable.SetIsBeingHeldServerRpc(false);
			}

			isHolding = false;
			grabbedRigidbody = null;
		}
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
				Debug.Log(hit.distance + " / " + " / " + NormalizeAndClamp(hit.distance, 0.0f, maxPushDistance) + " / " + pushFalloff.Evaluate(NormalizeAndClamp(hit.distance, 0.0f, maxPushDistance)) + " / " + EvaluatedPushForce.magnitude);

				if (hit.transform.TryGetComponent<NetworkObject>(out NetworkObject targetObjects))
					ApplyPushForceServerRpc(targetObjects.NetworkObjectId, EvaluatedPushForce, hit.point);
			}

			if (hit.transform.TryGetComponent(out InteractableView interactableView))
				interactableView.ShowEffectForTime(2.0f);
		}

		PlaySecondaryShootEffectEveryoneRpc();
		currentCharge = 0;
	}

	private void Update()
	{
		if (!IsOwner) return;

		if (isCharging) {
			currentCharge += Time.deltaTime;
		}
	}

	private void FixedUpdate()
	{
		if (!IsOwner) return;

		if (isHolding) {
			if (grabbedRigidbody.TryGetComponent<NetworkObject>(out NetworkObject targetObject)) {
				Vector3 ToTarget = grabPoint.position - grabbedRigidbody.position;

				float Distance = ToTarget.magnitude;

				//TODO This could make problems e.g. Lock the object (maybe only call once?)
				//if (Distance < arrivalThreshold) {
				//	SetGrabbedObjectVelocityServerRpc(targetObject.NetworkObjectId, Vector3.zero);
				//	return;
				//}

				Vector3 Direction = ToTarget.normalized;

				float Speed = maxSpeed;

				if (Distance < slowingRadius) {
					float t = Distance / slowingRadius;
					Speed = maxSpeed * (t * t);
				}

				Vector3 DesiredVelocity = Direction * Speed;

				Vector3 Steering = DesiredVelocity - grabbedRigidbody.linearVelocity;
				Steering = Vector3.ClampMagnitude(Steering, moveStrength);

				ApplyGrabForceServerRpc(targetObject.NetworkObjectId, Steering);
			}
		}
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
		if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObject, out NetworkObject targetObject)) {
			if (targetObject.TryGetComponent<Rigidbody>(out Rigidbody targetRigidbody)) {
				targetRigidbody.AddForceAtPosition(linearForce, forcePosition, ForceMode.Impulse);
			}
		}
	}

	[Rpc(SendTo.Server)]
	private void ApplyGrabForceServerRpc(ulong targetNetworkObject, Vector3 linearForce) {
		if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObject, out NetworkObject targetObject))
		{
			if (targetObject.TryGetComponent<Rigidbody>(out Rigidbody targetRigidbody))
			{
				targetRigidbody.AddForce(linearForce, ForceMode.Force);
			}
		}
	}

	[Rpc(SendTo.Server)]
	private void SetGrabbedObjectVelocityServerRpc(ulong targetNetworkObject, Vector3 velocity) {
		if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObject, out NetworkObject targetObject))
		{
			if (targetObject.TryGetComponent<Rigidbody>(out Rigidbody targetRigidbody))
			{
				targetRigidbody.linearVelocity = velocity;
			}
		}
	}
}
