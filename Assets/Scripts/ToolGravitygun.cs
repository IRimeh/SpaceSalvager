using System;
using UnityEngine;
using Unity.Netcode;
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
	private float spherecastRadius = 1.0f;

	[SerializeField]
	private AnimationCurve pushFalloff = new();

	private bool isHolding;
	private bool isCharging;
	private float currentCharge;
	private Transform playerCamera;

	public float CurrentCharge01 => Mathf.Clamp01(currentCharge / timeToMaxCharge);
	public event Action<float> OnShootEvent = delegate { };

	protected override void OnNetworkPostSpawn() 
	{
		playerCamera = transform.parent.parent;
	}

	public override void PressPrimary()
	{
		isHolding = true;
	}

	public override void ReleasePrimary()
	{
		isHolding = false;
	}

	public override void PressSecondary()
	{
		isCharging = true;
		currentCharge = 0.0f;
	}

	public override void ReleaseSecondary()
	{
		isCharging = false;

		float PushForce = ((maxPushforce - minPushforce) * CurrentCharge01) + minPushforce;

		RaycastHit[] hits = Physics.SphereCastAll(playerCamera.position, spherecastRadius, playerCamera.forward, maxPushDistance);

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
				interactableView.ShowEffectForTime(5.0f);
		}

		OnShootEvent.Invoke(CurrentCharge01);
		currentCharge = 0;
	}

	private void Update()
	{
		if (!IsOwner) return;

		if (isCharging) {
			currentCharge += Time.deltaTime;
		}
	}

	public static float NormalizeAndClamp(float value, float min, float max)
	{
		if (max == min) return 0f;

		float normalized = (value - min) / (max - min);
		return Mathf.Clamp(normalized, 0f, 1f);
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
}
