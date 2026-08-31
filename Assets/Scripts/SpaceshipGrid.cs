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

	private void RecalculatePhysics()
	{
		if (!IsServer) return;

		Rigidbody rb = GetComponent<Rigidbody>();
		if (rb == null) return;

		SpaceshipPart[] attachedParts = GetComponentsInChildren<SpaceshipPart>();

		if (attachedParts.Length == 0) return;

		float totalMass = 0f;
		Vector3 worldCenterOfMass = Vector3.zero;

		foreach (SpaceshipPart part in attachedParts)
		{
			totalMass += part.PartMass;
			worldCenterOfMass += part.transform.position * part.PartMass;
		}

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
