using System;
using Unity.Netcode;
using UnityEngine;

public class ChangeOwnerOnTouch : NetworkBehaviour
{
    protected override void OnNetworkPostSpawn()
    {
        if (!IsServer)
        {
            Destroy(this);
        }
    }

    private void OnCollisionEnter(Collision other)
    {
        if (other.rigidbody.TryGetComponent(out NetworkObject networkObject))
        {
            NetworkObject.ChangeOwnership(networkObject.OwnerClientId);
            Debug.Log($"Changed owner to: {networkObject.OwnerClientId}");
        }
    }
}
