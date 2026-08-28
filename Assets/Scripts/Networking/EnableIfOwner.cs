using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class EnableIfOwner : NetworkBehaviour
{
    [SerializeField] private List<GameObject> _toEnable;
    [SerializeField] private bool _invert = false;

    protected override void OnNetworkPostSpawn()
    {
        base.OnNetworkPostSpawn();
        foreach (GameObject gameObjectToEnable in _toEnable)
        {
            gameObjectToEnable.SetActive(_invert ? !IsOwner : IsOwner);
        }
    }
}
