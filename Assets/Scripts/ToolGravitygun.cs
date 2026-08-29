using UnityEngine;
using Unity.Netcode;

public class ToolGravitygun : Tool
{
	[SerializeField]
	private float moveStrength;

	protected override void OnNetworkPostSpawn() { 
	
	}
}
