using UnityEngine;
using Unity.Netcode;

public abstract class Tool : NetworkBehaviour
{
	public abstract void PressPrimary();
	public abstract void ReleasePrimary();
	public abstract void PressSecondary();
	public abstract void ReleaseSecondary();
}
