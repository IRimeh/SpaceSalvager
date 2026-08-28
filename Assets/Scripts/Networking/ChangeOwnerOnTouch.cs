using System;
using Unity.Netcode;
using UnityEngine;

public class ChangeOwnerOnTouch : NetworkBehaviour
{
    private void OnCollisionEnter(Collision other)
    {
        if (!IsOwner)
            return;
        
        if(other.gameObject.TryGetComponent(out NetworkObject networkObject))
            NetworkObject.ChangeOwnership(networkObject.OwnerClientId);
    }
}
