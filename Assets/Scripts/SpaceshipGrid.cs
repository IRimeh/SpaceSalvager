using NUnit.Framework;
using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

public class SpaceshipGrid : NetworkBehaviour
{
	public override void OnNetworkSpawn()
	{
		SpaceshipPart[] allParts = GetComponentsInChildren <SpaceshipPart> ();

		for (int i = 0; i < allParts.Length; i++)
		{
			allParts[i].SpaceshipPartId = i;
		}

		RecalculatePhysics();
	}

	public void SeverConnection(SpaceshipPart partA, SpaceshipPart partB) {
		if (!IsServer) return;

		partA.connectedParts.Remove(partB);
		partB.connectedParts.Remove(partA);

		if (!IsReachable(partA, partB)) {
			List<SpaceshipPart> detachedClusterA = GetConnectedCluster(partA);
			List<SpaceshipPart> detachedClusterB = GetConnectedCluster(partB);

			if(detachedClusterA.Count < detachedClusterB.Count)
			{
				MoveToNewGrid(detachedClusterA);
			} 
			else
			{
				MoveToNewGrid(detachedClusterB);
			}

			
		}
	}

	private bool IsReachable(SpaceshipPart start, SpaceshipPart target) {
		HashSet<SpaceshipPart> visited = new HashSet<SpaceshipPart>();
		Queue<SpaceshipPart> queue = new Queue<SpaceshipPart>();

		queue.Enqueue(start);
		visited.Add(start);

		while (queue.Count > 0) {
			SpaceshipPart current = queue.Dequeue();
			if (current == target) return true;

			foreach (SpaceshipPart neighbour in current.connectedParts)
			{
				if (!visited.Contains(neighbour)) {
					visited.Add(neighbour);
					queue.Enqueue(neighbour);
				}
			}
		}

		return false;
	}

	private List<SpaceshipPart> GetConnectedCluster(SpaceshipPart start) {
		List<SpaceshipPart> cluster = new List<SpaceshipPart>();
		HashSet<SpaceshipPart> visited = new HashSet<SpaceshipPart>();
		Queue<SpaceshipPart> queue = new Queue<SpaceshipPart>();

		queue.Enqueue(start);
		visited.Add(start);

		while (queue.Count > 0)
		{
			SpaceshipPart current = queue.Dequeue();
			cluster.Add(current);

			foreach (SpaceshipPart neighbour in current.connectedParts)
			{
				if (!visited.Contains(neighbour))
				{
					visited.Add(neighbour);
					queue.Enqueue(neighbour);
				}
			}
		}

		return cluster;
	}

	/// <summary>
	/// Server-authoritative removal of a part from this grid. Detaches the part from the
	/// connection graph, splits the remaining ship into separate grids if removing the
	/// part disconnected it, then destroys only the evaporated part on every peer.
	/// </summary>
	public void EvaporatePart(SpaceshipPart part)
	{
		if (!IsServer || part == null) return;

		// 1. Detach the part from the graph WITHOUT triggering the per-connection split
		// logic. The evaporating part must never join a cluster or be moved to a new grid.
		foreach (SpaceshipPart neighbour in new List<SpaceshipPart>(part.connectedParts))
		{
			if (neighbour != null)
			{
				neighbour.connectedParts.Remove(part);
			}
		}
		part.connectedParts.Clear();

		// 2. If the remaining ship is no longer connected, keep the largest cluster on
		// this grid and move each other cluster to its own grid.
		SplitOffDisconnectedClusters(part);

		// 3. Destroy only this part on every peer. The part never leaves this grid, so
		// clients can find it among this grid's children by its SpaceshipPartId.
		EvaporatePartClientRpc(part.SpaceshipPartId);
		Destroy(part.gameObject);

		// 4. Update physics without the evaporated part (Destroy is deferred to end of frame).
		RecalculatePhysics(part);
	}

	private void SplitOffDisconnectedClusters(SpaceshipPart removedPart)
	{
		HashSet<SpaceshipPart> visited = new HashSet<SpaceshipPart> { removedPart };
		List<List<SpaceshipPart>> clusters = new List<List<SpaceshipPart>>();

		foreach (SpaceshipPart part in GetComponentsInChildren<SpaceshipPart>())
		{
			if (visited.Contains(part)) continue;

			List<SpaceshipPart> cluster = GetConnectedCluster(part, visited);
			if (cluster.Count > 0)
			{
				clusters.Add(cluster);
			}
		}

		if (clusters.Count <= 1) return;

		clusters.Sort((a, b) => b.Count.CompareTo(a.Count));
		for (int i = 1; i < clusters.Count; i++)
		{
			MoveToNewGrid(clusters[i]);
		}
	}

	private List<SpaceshipPart> GetConnectedCluster(SpaceshipPart start, HashSet<SpaceshipPart> visited)
	{
		List<SpaceshipPart> cluster = new List<SpaceshipPart>();
		Queue<SpaceshipPart> queue = new Queue<SpaceshipPart>();

		queue.Enqueue(start);
		visited.Add(start);

		while (queue.Count > 0)
		{
			SpaceshipPart current = queue.Dequeue();
			cluster.Add(current);

			foreach (SpaceshipPart neighbour in current.connectedParts)
			{
				if (neighbour != null && !visited.Contains(neighbour))
				{
					visited.Add(neighbour);
					queue.Enqueue(neighbour);
				}
			}
		}

		return cluster;
	}

	[Rpc(SendTo.Everyone)]
	private void EvaporatePartClientRpc(int partId)
	{
		if (IsServer) return;

		foreach (SpaceshipPart part in GetComponentsInChildren<SpaceshipPart>())
		{
			if (part.SpaceshipPartId == partId)
			{
				Destroy(part.gameObject);
				break;
			}
		}
	}

	private void MoveToNewGrid(List<SpaceshipPart> detachedParts)
	{
		Vector3 spawnPosition = detachedParts[0].transform.position;

		GameObject newSpaceshipGrid = Instantiate(GetCleanPrefabFromNetworkManager(), spawnPosition, Quaternion.identity);

		newSpaceshipGrid.GetComponent<NetworkObject>().Spawn();

		int[] detachedPartIds = new int[detachedParts.Count];

		for (int i = 0; i < detachedParts.Count; i++)
		{
			SpaceshipPart part = detachedParts[i];

			part.transform.SetParent(newSpaceshipGrid.transform, true);

			detachedPartIds[i] = part.SpaceshipPartId;
		}

		newSpaceshipGrid.GetComponent<SpaceshipGrid>().RecalculatePhysics();
		RecalculatePhysics();

		Rigidbody originalRb = GetComponent<Rigidbody>();
		Rigidbody newRb = newSpaceshipGrid.GetComponent<Rigidbody>();

		if (originalRb != null && newRb != null)
		{
			// 1. Calculate the exact linear velocity at the new grid's center of mass
			// This accounts for both the original ship's movement AND its rotation (tangential velocity)
			newRb.linearVelocity = originalRb.GetPointVelocity(newRb.worldCenterOfMass);

			// 2. Both grids maintain the exact same spin rate they had before the split
			newRb.angularVelocity = originalRb.angularVelocity;
		}

		MovePartsClientRpc(newSpaceshipGrid.GetComponent<NetworkObject>().NetworkObjectId, detachedPartIds);
	}

	[Rpc(SendTo.Everyone)]
	private void MovePartsClientRpc(ulong newGridNetworkId, int[] partIdsToMove)
	{
		if (IsServer) return;

		if(NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(newGridNetworkId, out NetworkObject newGridNetworkObject))
		{
			SpaceshipPart[] currentParts = GetComponentsInChildren<SpaceshipPart>();

			foreach (int idToMove in partIdsToMove)
			{
				foreach (SpaceshipPart part in currentParts)
				{
					if(part.SpaceshipPartId == idToMove)
					{
						part.transform.SetParent(newGridNetworkObject.transform, true);
						break;
					}
				}
			}
		}
	}

	private void RecalculatePhysics(SpaceshipPart partToExclude = null)
	{
		if (!IsServer) return;

		Rigidbody rb = GetComponent<Rigidbody>();
		if (rb == null) return;

		SpaceshipPart[] attachedParts = GetComponentsInChildren<SpaceshipPart>();

		float totalMass = 0f;
		Vector3 worldCenterOfMass = Vector3.zero;
		int countedParts = 0;

		foreach (SpaceshipPart part in attachedParts)
		{
			if (part == partToExclude) continue;

			totalMass += part.PartMass;
			worldCenterOfMass += part.transform.position * part.PartMass;
			countedParts++;
		}

		if (countedParts == 0) return;

		worldCenterOfMass /= totalMass;

		rb.mass = totalMass;

		rb.centerOfMass = transform.InverseTransformPoint(worldCenterOfMass);

		rb.WakeUp();
	}

	private GameObject GetCleanPrefabFromNetworkManager()
	{
		// Iterate through all prefabs you registered in the NetworkManager
		foreach (var networkPrefab in NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
		{
			if (networkPrefab.Prefab != null)
			{
				// Match the prefab by its exact Asset name
				if (networkPrefab.Prefab.name == "pfb_SpaceShipGrid")
				{
					return networkPrefab.Prefab;
				}
			}
		}

		Debug.LogError("SpaceshipGrid prefab not found! Make sure it is added to the NetworkManager's Network Prefabs list and the name matches exactly.");
		return null;
	}
}
