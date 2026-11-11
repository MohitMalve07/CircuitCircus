using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Server-side spawn manager that assigns fixed positions to players based on join order.
/// Attach this to a GameObject in the Game scene.
/// 
/// IMPORTANT — Unity Netcode Setup:
/// 1. Set the Player Prefab on the NetworkManager.
/// 2. UNCHECK "Auto-Spawn Player Prefab" on the NetworkManager so this script controls spawning.
/// 3. Ensure the Player Prefab has a NetworkObject component.
/// </summary>
public class SpawnManager : NetworkBehaviour
{
    [Header("Spawn Settings")]
    [Tooltip("The player prefab to spawn (must have a NetworkObject component)")]
    public GameObject playerPrefab;

    [Tooltip("Maximum number of players allowed in the session")]
    public int maxPlayers = 4;

    // Fixed spawn positions indexed by join order (0-based)
    private readonly Vector3[] spawnPositions = new Vector3[]
    {
        new Vector3(223.830002f, 8.369382501f, -143.339996f),   // Player 1
        new Vector3(220.330002f, 8.369382501f, -148.217575f),   // Player 2
        new Vector3(223.580002f, 8.369382501f, -152.967575f),   // Player 3
        new Vector3(220.330002f, 8.369382501f, -157.967575f)    // Player 4
    };

    // Default rotation for all spawned players
    private readonly Quaternion spawnRotation = Quaternion.identity;

    // Tracks which spawn slot index each connected client occupies
    private readonly Dictionary<ulong, int> assignedSlots = new Dictionary<ulong, int>();
    private readonly bool[] slotOccupied = new bool[4];

    // Tracks spawned player objects for cleanup
    private readonly Dictionary<ulong, NetworkObject> spawnedPlayers = new Dictionary<ulong, NetworkObject>();

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        ResetSlotTracking();

        // Subscribe to connection events so we handle spawning ourselves
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        // Spawn players for ALL currently-connected clients.
        // This handles both the host AND any clients that connected
        // before this scene finished loading (race condition fix).
        // The duplicate-spawn guard in OnClientConnected prevents double-spawning.
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            OnClientConnected(clientId);
        }
    }

    /// <summary>
    /// Called on the server when a client connects. Spawns a player at the first available slot.
    /// </summary>
    private void OnClientConnected(ulong clientId)
    {
        // Prevent duplicate spawns (e.g. host calling this twice)
        if (spawnedPlayers.ContainsKey(clientId))
        {
            Debug.LogWarning($"[SpawnManager] Client {clientId} already has a spawned player. Skipping.");
            return;
        }

        int slotIndex = FindAvailableSlotIndex();
        if (slotIndex < 0)
        {
            Debug.LogWarning($"[SpawnManager] Max players ({Mathf.Min(maxPlayers, spawnPositions.Length)}) reached. Disconnecting client {clientId}.");
            NetworkManager.Singleton.DisconnectClient(clientId);
            return;
        }

        if (playerPrefab == null)
        {
            Debug.LogError("[SpawnManager] Player prefab is not assigned!");
            return;
        }

        // Spawn the player at the assigned position
        Vector3 position = spawnPositions[slotIndex];
        GameObject playerInstance = Instantiate(playerPrefab, position, spawnRotation);

        NetworkObject networkObject = playerInstance.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogError("[SpawnManager] Player prefab is missing a NetworkObject component!");
            Destroy(playerInstance);
            return;
        }

        // Spawn as player object — this assigns ownership to the connecting client
        networkObject.SpawnAsPlayerObject(clientId);

        // Track the spawned player and occupied slot
        spawnedPlayers[clientId] = networkObject;
        assignedSlots[clientId] = slotIndex;
        slotOccupied[slotIndex] = true;

        Debug.Log($"[SpawnManager] Player slot {slotIndex + 1} (Client {clientId}) spawned at {position}");
    }

    /// <summary>
    /// Called on the server when a client disconnects. Cleans up their player object and frees their spawn slot.
    /// </summary>
    private void OnClientDisconnected(ulong clientId)
    {
        if (spawnedPlayers.TryGetValue(clientId, out NetworkObject networkObject))
        {
            if (networkObject != null && networkObject.IsSpawned)
            {
                networkObject.Despawn();
                Destroy(networkObject.gameObject);
            }

            spawnedPlayers.Remove(clientId);
            Debug.Log($"[SpawnManager] Cleaned up player for Client {clientId}.");
        }

        if (assignedSlots.TryGetValue(clientId, out int slotIndex))
        {
            if (slotIndex >= 0 && slotIndex < slotOccupied.Length)
            {
                slotOccupied[slotIndex] = false;
            }
            assignedSlots.Remove(clientId);
            Debug.Log($"[SpawnManager] Freed spawn slot {slotIndex + 1} from Client {clientId}.");
        }
    }

    private int FindAvailableSlotIndex()
    {
        int effectiveMax = Mathf.Clamp(maxPlayers, 0, Mathf.Min(spawnPositions.Length, slotOccupied.Length));
        for (int i = 0; i < effectiveMax; i++)
        {
            if (!slotOccupied[i])
            {
                return i;
            }
        }
        return -1;
    }

    private void ResetSlotTracking()
    {
        spawnedPlayers.Clear();
        assignedSlots.Clear();
        for (int i = 0; i < slotOccupied.Length; i++)
        {
            slotOccupied[i] = false;
        }
    }

    public override void OnNetworkDespawn()
    {
        // Unsubscribe to prevent memory leaks
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        ResetSlotTracking();
    }
}
