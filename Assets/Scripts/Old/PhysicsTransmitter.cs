using UnityEngine;
using Unity.Netcode;
using System;

[RequireComponent(typeof(Rigidbody))]
public class PhysicsTransmitter : NetworkBehaviour
{
	private Rigidbody playerRigidbody;

	protected override void OnNetworkPostSpawn()
	{
		playerRigidbody = GetComponent<Rigidbody>();
	}

	private void OnCollisionEnter(Collision collision)
	{
		if (!IsOwner) return;

		if (collision.collider.TryGetComponent(out NetworkObject hitNetObject))
		{
			if (!hitNetObject.IsOwner && hitNetObject.IsSpawned)
			{
				if (hitNetObject.TryGetComponent<Rigidbody>(out Rigidbody targetRigidbody))
				{
					Vector3 impactForce = collision.relativeVelocity * playerRigidbody.mass; // Bump force multiplier?
					Vector3 contactPoint = collision.GetContact(0).point;

					PushObjectServerRpc(hitNetObject.NetworkObjectId, impactForce, contactPoint);
				}
			}
		}
	}

	[Rpc(SendTo.Server)]
	private void PushObjectServerRpc(ulong targetNetworkObjectId, Vector3 force, Vector3 position)
	{
		if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObject)) 
		{
			if (targetObject.TryGetComponent<Rigidbody>(out Rigidbody targetRigidbody)) 
			{
				Debug.Log("Applied force on server");

				targetRigidbody.AddForceAtPosition(force, position, ForceMode.Impulse);
			}
		}
	}
}
