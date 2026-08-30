using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEditor;
using UnityEngine;

public class UGSManager : MonoBehaviour
{
	private const string KEY_RELAY_CODE = "RelayJoinCode";
	private string currentLobbyId;

	public static event Action OnConnect = delegate { };

	private async void Start()
	{
		// 1. Initialize UGS Core and Authenticate Anonymously
		await UnityServices.InitializeAsync();
		if (!AuthenticationService.Instance.IsSignedIn)
		{
			await AuthenticationService.Instance.SignInAnonymouslyAsync();
			Debug.Log($"Signed in as Player ID: {AuthenticationService.Instance.PlayerId}");
		}
	}

	// --- HOST FLOW ---
	public async void CreateLobbyAndHost(int maxPlayers = 4)
	{
		try
		{
			// 1. Allocation on Relay Server
			Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
			string relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

			// 2. Configure UnityTransport with Relay Data
			NetworkManager.Singleton.GetComponent<UnityTransport>()
				.SetHostRelayData(
					allocation.RelayServer.IpV4,
					(ushort)allocation.RelayServer.Port,
					allocation.AllocationIdBytes,
					allocation.Key,
					allocation.ConnectionData
				);

			// 3. Create Lobby and store Relay Code in Data
			CreateLobbyOptions options = new CreateLobbyOptions
			{
				IsPrivate = false,
				Data = new Dictionary<string, DataObject>
				{
					{
						KEY_RELAY_CODE,
						new DataObject(DataObject.VisibilityOptions.Public, relayJoinCode)
					}
				}
			};

			Lobby lobby = await LobbyService.Instance.CreateLobbyAsync("My Room", maxPlayers, options);
			currentLobbyId = lobby.Id;

			// Send periodic heartbeats so the lobby stays active in UGS
			InvokeRepeating(nameof(SendLobbyHeartbeat), 15f, 15f);

			// 4. Start Host
			NetworkManager.Singleton.StartHost();
			OnConnect.Invoke();
			Debug.Log($"Lobby created! Room Join Code: {lobby.LobbyCode}");
			GUIUtility.systemCopyBuffer = lobby.LobbyCode;

			// Update UI status with the lobby code
			FindObjectOfType<LobbyUIController>()?.UpdateStatus($"Lobby Created! Code: {lobby.LobbyCode}");
		}
		catch (LobbyServiceException e)
		{
			Debug.LogError($"UGS Lobby Error: {e.Message}");
		}
	}

	// --- CLIENT FLOW ---
	public async void JoinLobbyByCode(string lobbyCode)
	{
		try
		{
			// 1. Join UGS Lobby via Code entered by user
			Lobby lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode);
			string relayJoinCode = lobby.Data[KEY_RELAY_CODE].Value;

			// 2. Join Relay Server using extracted Relay Code
			JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayJoinCode);

			// 3. Configure UnityTransport for Client
			NetworkManager.Singleton.GetComponent<UnityTransport>()
				.SetClientRelayData(
					joinAllocation.RelayServer.IpV4,
					(ushort)joinAllocation.RelayServer.Port,
					joinAllocation.AllocationIdBytes,
					joinAllocation.Key,
					joinAllocation.ConnectionData,
					joinAllocation.HostConnectionData
				);

			// 4. Start Client
			NetworkManager.Singleton.StartClient();
			OnConnect.Invoke();
		}
		catch (LobbyServiceException e)
		{
			Debug.LogError($"Failed to join lobby: {e.Message}");
		}
	}

	private async void SendLobbyHeartbeat()
	{
		if (!string.IsNullOrEmpty(currentLobbyId))
		{
			await LobbyService.Instance.SendHeartbeatPingAsync(currentLobbyId);
		}
	}

	private void OnDestroy()
	{
		CancelInvoke(nameof(SendLobbyHeartbeat));
	}
}