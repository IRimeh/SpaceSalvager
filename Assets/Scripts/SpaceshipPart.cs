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

	[Header("Mass")]
	[Tooltip("Library that defines the available materials and their densities.")]
	public PartMaterialLibrary materialLibrary;

	[Tooltip("Name of the material (from the library) this part is made from.")]
	public string materialName;

	[Tooltip("Auto-calculated as mesh volume * material density. Read-only.")]
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

	/// <summary>
	/// Recomputes <see cref="PartMass"/> from the mesh volume and the density
	/// of the selected material in the assigned <see cref="PartMaterialLibrary"/>.
	/// </summary>
	public void RecalculateMass()
	{
		float density = materialLibrary != null ? materialLibrary.GetDensity(materialName) : 0f;
		PartMass = CalculateVolume() * density;
	}

	/// <summary>
	/// Returns the volume of this part's mesh in world units, accounting for the
	/// object's scale. Uses the signed-tetrahedron method summed over all triangles.
	/// </summary>
	public float CalculateVolume()
	{
		MeshFilter meshFilter = GetComponent<MeshFilter>();
		if (meshFilter == null || meshFilter.sharedMesh == null)
			return 0f;

		Mesh mesh = meshFilter.sharedMesh;
		Vector3[] vertices = mesh.vertices;
		int[] triangles = mesh.triangles;

		float volume = 0f;
		for (int i = 0; i < triangles.Length; i += 3)
		{
			Vector3 p1 = vertices[triangles[i]];
			Vector3 p2 = vertices[triangles[i + 1]];
			Vector3 p3 = vertices[triangles[i + 2]];
			volume += Vector3.Dot(p1, Vector3.Cross(p2, p3)) / 6f;
		}

		volume = Mathf.Abs(volume);

		Vector3 scale = transform.lossyScale;
		return volume * scale.x * scale.y * scale.z;
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