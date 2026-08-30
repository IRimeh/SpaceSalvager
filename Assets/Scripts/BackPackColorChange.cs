using System;
using Unity.Netcode;
using UnityEngine;

public class BackPackColorChange : NetworkBehaviour
{
	//TODO This shit aint working fam

	[SerializeField]
	private Color[] colors;

	private NetworkVariable<int> nextColor = new NetworkVariable<int>(0);

	protected override void OnNetworkPostSpawn()
	{
		nextColor.OnValueChanged += ReceiveColor;

		RequestColorServerRpc();
	}

	private void ReceiveColor(int oldValue, int newValue)
	{
		GetComponent<MeshRenderer>().material.SetColor("_BaseColor", colors[oldValue]);
		nextColor.OnValueChanged -= ReceiveColor;
	}

	[Rpc(SendTo.Server)]
	private void RequestColorServerRpc() {
		nextColor.Value++;
	}
}
