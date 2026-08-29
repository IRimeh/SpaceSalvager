using Unity.Netcode;
using UnityEngine;

public class Interactable : NetworkBehaviour
{
	public NetworkVariable<bool> IsBeingHeld = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
	public NetworkVariable<bool> IsBeingHeldByLocalPlayer = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
}
