using Unity.Netcode;

public class Interactable : NetworkBehaviour
{
	public NetworkVariable<bool> IsBeingHeld = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
	public bool IsBeingHeldByLocalPlayer = false;
}
