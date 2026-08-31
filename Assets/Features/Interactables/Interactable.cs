using System;
using Unity.Netcode;

public class Interactable : NetworkBehaviour
{
	public NetworkVariable<bool> IsBeingHeld = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
	public bool IsBeingHeldByLocalPlayer = false;
	public event Action OnStartHolding = delegate { };

	[Rpc(SendTo.Server)]
	public void SetIsBeingHeldServerRpc(bool isBeingHeld)
	{
		IsBeingHeld.Value = isBeingHeld;
		if(isBeingHeld)
			OnStartHolding.Invoke();
	}
}
