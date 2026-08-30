using Unity.Netcode;
using UnityEngine;

public class DisableOnConnect : NetworkBehaviour
{
    protected override void OnNetworkPostSpawn()
    {
        base.OnNetworkPostSpawn();
        gameObject.SetActive(false);
    }
}
