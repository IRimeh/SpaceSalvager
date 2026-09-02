using System;
using UnityEditor;
using UnityEngine;
using Unity.Netcode;
using System.Reflection.Metadata.Ecma335;

public class Interactable : NetworkBehaviour
{
	public NetworkVariable<bool> IsBeingHeld = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
	public bool IsBeingHeldByLocalPlayer = false;
	public event Action OnStartHolding = delegate { };

	[SerializeField]
	private bool isStatic = false;

	public bool GetIsStatic() {
		return isStatic;
	}

	[Rpc(SendTo.Server)]
	public void SetIsBeingHeldServerRpc(bool isBeingHeld)
	{
		IsBeingHeld.Value = isBeingHeld;
		if(isBeingHeld)
			OnStartHolding.Invoke();
	}
}
