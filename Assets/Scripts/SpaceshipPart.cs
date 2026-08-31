using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;

[RequireComponent(typeof(MeshRenderer))]
public class SpaceshipPart : NetworkBehaviour
{
	[HideInInspector]
	public int SpaceshipPartId;

	public List<SpaceshipPart> connectedParts = new();

	private MeshRenderer meshRenderer = null;

	public float PartMass = 10f;

	private void OnEnable()
	{
		meshRenderer = GetComponent<MeshRenderer>();

		for (int i = connectedParts.Count - 1; i >= 0; i--)
		{
			SpaceshipPart part = connectedParts[i];
			if (!part.connectedParts.Contains(this))
			{
				connectedParts.Remove(part);
			}
		}
	}

	public void OnValidate()
	{
		meshRenderer = GetComponent<MeshRenderer>();

		for (int i = connectedParts.Count - 1; i >= 0; i--)
		{
			SpaceshipPart part = connectedParts[i];
			if (!part.connectedParts.Contains(this))
			{
				connectedParts.Remove(part);
			}
		}
	}

	private void OnDrawGizmos()
	{
		Gizmos.color = Color.cyan;

		foreach (SpaceshipPart part in connectedParts)
		{
			if (part != null)
			{
				Gizmos.DrawLine(meshRenderer.bounds.center, part.meshRenderer.bounds.center);
				Gizmos.DrawCube(meshRenderer.bounds.center, Vector3.one * 0.2f);
			}
		}
	}

	public void SeverPartFromAll()
	{
		// If we are a client, request the server to handle the severing
		if (!IsServer)
		{
			SeverPartFromAllServerRpc();
			return;
		}

		// Server logic runs directly
		ExecuteSeverPartFromAll();
	}

	[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
	private void SeverPartFromAllServerRpc()
	{
		ExecuteSeverPartFromAll();
	}

	private void ExecuteSeverPartFromAll()
	{
		SpaceshipGrid parentGrid = GetComponentInParent<SpaceshipGrid>();
		if (parentGrid == null) return;

		List<SpaceshipPart> connections = new List<SpaceshipPart>(connectedParts);

		foreach (SpaceshipPart neighbour in connections)
		{
			if (neighbour != null)
			{
				parentGrid.SeverConnection(this, neighbour);
			}
		}
	}
}