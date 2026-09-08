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

	[SerializeField]
	private int cutResistance = 1;

	[SerializeField]
	private bool sliceable = false;

	[Header("Mass")]
	[Tooltip("Library that defines the available materials and their densities.")]
	public PartMaterialLibrary materialLibrary;

	[Tooltip("Name of the material (from the library) this part is made from.")]
	public string materialName;

	[Tooltip("Auto-calculated as mesh volume * material density. Read-only.")]
	public float PartMass = 10f;

	[Header("Heat / Cutting")]
	[Tooltip("Multiplier turning PartMass into the total heat required to evaporate. heatNeeded = PartMass * heatPerMass.")]
	[SerializeField]
	private float heatPerMass = 1f;

	[Tooltip("How fast the part cools (in heat01 units per second) when no cutter is heating it. Should be higher than typical heat rate so cooling is faster than heating.")]
	[SerializeField]
	private float coolSpeed01 = 1.5f;

	[Tooltip("Seconds without a heartbeat before a cutter contributor is dropped (handles release, look-away, target switch, disconnect).")]
	[SerializeField]
	private float contributorTimeout = 0.3f;

	[Tooltip("Minimum interval between server -> clients heat broadcasts.")]
	[SerializeField]
	private float heatBroadcastInterval = 0.1f;

	[Tooltip("Color the base color lerps toward as the part fully heats up.")]
	[SerializeField]
	private Color hotColor = Color.red;

	// Server-only: active cutter contributions keyed by client id.
	private struct HeatContribution
	{
		public int strength;
		public float lastSeen;
	}
	private readonly Dictionary<ulong, HeatContribution> contributors = new();

	// Server-authoritative heat [0,1]; targetHeat01 is the last broadcast value; displayHeat01 is the smoothed local visual.
	private float heat01;
	private float targetHeat01;
	private float displayHeat01;
	private float lastBroadcastTime;
	private float lastBroadcastValue = -1f;

	// Visual state (all peers).
	private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
	private MaterialPropertyBlock heatPropertyBlock;
	private Color originalBaseColor = Color.white;
	private bool capturedOriginalColor;
	private bool heatOverrideActive;

	private void OnEnable()
	{
		meshRenderer = GetComponent<MeshRenderer>();
		CaptureOriginalBaseColor();

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

	public void EvaporatePart()
	{
		// Evaporation is server-authoritative in Netcode for GameObjects.
		// If we are a client, ask the server to perform the evaporation.
		if (!IsServer)
		{
			EvaporatePartServerRpc();
			return;
		}

		ExecuteEvaporatePart();
	}

	[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
	private void EvaporatePartServerRpc()
	{
		ExecuteEvaporatePart();
	}

	private void ExecuteEvaporatePart()
	{
		// A SpaceshipPart has no NetworkObject of its own - NetworkObject resolves to the
		// parent grid, so despawning it would destroy the whole remaining ship. Instead,
		// hand off to the grid: it detaches this part, splits the remaining ship if it
		// became disconnected, and destroys only this part on every peer.
		// GetComponentInParent resolves the grid this part is CURRENTLY parented under,
		// which also covers parts that were moved to a new grid by an earlier split.
		SpaceshipGrid parentGrid = GetComponentInParent<SpaceshipGrid>();
		if (parentGrid != null)
		{
			parentGrid.EvaporatePart(this);
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

	// ---------------------------------------------------------------------
	// Heat-up / cool-down cutting
	// ---------------------------------------------------------------------

	private void CaptureOriginalBaseColor()
	{
		if (capturedOriginalColor) return;
		if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
		if (meshRenderer == null || meshRenderer.sharedMaterial == null) return;

		if (meshRenderer.sharedMaterial.HasProperty(BaseColorId))
			originalBaseColor = meshRenderer.sharedMaterial.GetColor(BaseColorId);

		capturedOriginalColor = true;
	}

	/// <summary>
	/// Client -> Server heartbeat: a cutter is currently heating this part with the given strength.
	/// Contributors are keyed by client id and time out automatically if heartbeats stop.
	/// </summary>
	[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
	public void HeatContributionServerRpc(int strength, RpcParams rpcParams = default)
	{
		ulong senderId = rpcParams.Receive.SenderClientId;
		contributors[senderId] = new HeatContribution { strength = strength, lastSeen = Time.time };
	}

	private void Update()
	{
		if (IsServer)
			ServerUpdateHeat();

		if (IsClient || IsServer)
			ClientUpdateHeatVisual();
	}

	private void ServerUpdateHeat()
	{
		// 1. Drop stale contributors (release / look-away / target switch / disconnect).
		if (contributors.Count > 0)
		{
			float now = Time.time;
			// Copy keys to avoid mutating the dictionary while iterating.
			List<ulong> stale = null;
			foreach (KeyValuePair<ulong, HeatContribution> kvp in contributors)
			{
				if (now - kvp.Value.lastSeen > contributorTimeout)
				{
					stale ??= new List<ulong>();
					stale.Add(kvp.Key);
				}
			}
			if (stale != null)
				foreach (ulong id in stale)
					contributors.Remove(id);
		}

		// 2. Total heat rate = sum of (strength - cutResistance) for contributors above resistance.
		int totalRate = 0;
		foreach (KeyValuePair<ulong, HeatContribution> kvp in contributors)
		{
			int effective = kvp.Value.strength - cutResistance;
			if (effective > 0) totalRate += effective;
		}

		float heatNeeded = Mathf.Max(0.0001f, PartMass * heatPerMass);

		if (totalRate > 0)
			heat01 += (totalRate / heatNeeded) * Time.deltaTime;
		else
			heat01 -= coolSpeed01 * Time.deltaTime;

		heat01 = Mathf.Clamp01(heat01);

		// 3. Evaporate when fully heated (reuses existing server-authoritative grid path).
		if (heat01 >= 1f)
		{
			heat01 = 1f;
			BroadcastHeat(force: true);
			ExecuteEvaporatePart();
			return;
		}

		// 4. Throttled broadcast to all peers when the value changed meaningfully.
		if (Time.time - lastBroadcastTime >= heatBroadcastInterval &&
			!Mathf.Approximately(heat01, lastBroadcastValue))
		{
			BroadcastHeat(force: false);
		}
	}

	private void BroadcastHeat(bool force)
	{
		lastBroadcastTime = Time.time;
		lastBroadcastValue = heat01;
		UpdateHeatClientRpc(heat01);
	}

	[Rpc(SendTo.Everyone)]
	private void UpdateHeatClientRpc(float value)
	{
		targetHeat01 = value;
	}

	private void ClientUpdateHeatVisual()
	{
		// Smoothly approach the last networked heat value.
		displayHeat01 = Mathf.MoveTowards(displayHeat01, targetHeat01, Mathf.Max(coolSpeed01, 2f) * Time.deltaTime);

		if (displayHeat01 <= 0.001f)
		{
			// Fully cool: clear any override so the exact original material shows again.
			if (heatOverrideActive && meshRenderer != null)
			{
				heatPropertyBlock ??= new MaterialPropertyBlock();
				meshRenderer.GetPropertyBlock(heatPropertyBlock);
				heatPropertyBlock.SetColor(BaseColorId, originalBaseColor);
				meshRenderer.SetPropertyBlock(heatPropertyBlock);
				heatOverrideActive = false;
			}
			return;
		}

		if (meshRenderer == null) return;
		CaptureOriginalBaseColor();

		heatPropertyBlock ??= new MaterialPropertyBlock();
		meshRenderer.GetPropertyBlock(heatPropertyBlock);
		heatPropertyBlock.SetColor(BaseColorId, Color.Lerp(originalBaseColor, hotColor, displayHeat01));
		meshRenderer.SetPropertyBlock(heatPropertyBlock);
		heatOverrideActive = true;
	}
}