using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using UnityEngine.SceneManagement;

/// <summary>
/// Dynamic multiplayer lobby UI that shows connected players and lets the host start the game.
/// Driven by <see cref="LobbyNetwork"/>'s replicated <see cref="NetworkList{T}"/> so both Host
/// and Clients see the full player roster and usernames before the Game scene loads.
///
/// SETUP:
/// 1. Create a Lobby panel (inactive by default) in the Main Menu Canvas.
/// 2. Inside it, add a Scroll View with a Content container.
/// 3. Create a PlayerEntry prefab (a UI element with a Legacy Text child).
/// 4. Place <see cref="LobbyNetwork"/> + <see cref="NetworkObject"/> on an active root object.
/// 5. Assign all references in the Inspector.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Prefab with a Text component for each player entry.")]
    public GameObject playerEntryPrefab;

    [Tooltip("The Content transform inside the Scroll View where player entries are spawned.")]
    public Transform contentParent;

    [Tooltip("Button to start the game. Only visible and usable by the Host.")]
    public Button startGameButton;

    [Tooltip("Optional text showing player count (e.g., 'Players: 2/4').")]
    public Text playerCountText;

    [Header("Settings")]
    [Tooltip("Name of the gameplay scene to load when the host starts the game.")]
    public string gameSceneName = "Game";

    [Tooltip("Maximum number of players allowed in the lobby.")]
    public int maxPlayers = 4;

    [Tooltip("How often (in seconds) to re-check lobby state as a fallback.")]
    public float refreshInterval = 0.5f;

    // Dictionary mapping clientId → instantiated UI entry
    private readonly Dictionary<ulong, GameObject> playerEntries = new Dictionary<ulong, GameObject>();

    private bool isHost = false;
    private bool isInitialized = false;
    private bool isStartingGame = false;
    private float refreshTimer = 0f;
    private LobbyNetwork subscribedLobbyNetwork;

    /// <summary>
    /// Called by NetworkUI after activating the lobby panel.
    /// Receives the host/client role directly.
    /// </summary>
    public void Initialize(bool hostMode)
    {
        isHost = hostMode;
        isInitialized = true;
        isStartingGame = false;
        refreshTimer = 0f;

        // Configure Start Game button
        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(isHost);
            startGameButton.interactable = isHost;

            if (isHost && startGameButton.onClick.GetPersistentEventCount() == 0)
            {
                startGameButton.onClick.RemoveAllListeners();
                startGameButton.onClick.AddListener(OnStartGameClicked);
                Debug.Log("[LobbyUI] Start Game button enabled for Host.");
            }
        }
        else
        {
            Debug.LogWarning("[LobbyUI] startGameButton is not assigned in Inspector!");
        }

        // Validate required references
        if (playerEntryPrefab == null)
            Debug.LogError("[LobbyUI] playerEntryPrefab is not assigned in Inspector!");
        if (contentParent == null)
            Debug.LogError("[LobbyUI] contentParent is not assigned in Inspector!");

        SubscribeToLobbyNetwork();
        RefreshPlayerList();

        Debug.Log($"[LobbyUI] Initialized as {(isHost ? "HOST" : "CLIENT")}.");
    }

    private void Update()
    {
        if (!isInitialized) return;

        refreshTimer += Time.deltaTime;
        if (refreshTimer >= refreshInterval)
        {
            refreshTimer = 0f;
            RefreshPlayerList();
        }
    }

    private void SubscribeToLobbyNetwork()
    {
        LobbyNetwork target = LobbyNetwork.Instance != null
            ? LobbyNetwork.Instance
            : GetComponent<LobbyNetwork>();

        if (subscribedLobbyNetwork == target) return;

        if (subscribedLobbyNetwork != null)
        {
            subscribedLobbyNetwork.OnLobbyPlayersChanged -= HandleLobbyPlayersChanged;
        }

        subscribedLobbyNetwork = target;

        if (subscribedLobbyNetwork != null)
        {
            subscribedLobbyNetwork.OnLobbyPlayersChanged += HandleLobbyPlayersChanged;
        }
    }

    private void UnsubscribeFromLobbyNetwork()
    {
        if (subscribedLobbyNetwork != null)
        {
            subscribedLobbyNetwork.OnLobbyPlayersChanged -= HandleLobbyPlayersChanged;
            subscribedLobbyNetwork = null;
        }
    }

    private void HandleLobbyPlayersChanged()
    {
        if (!isInitialized) return;
        RefreshPlayerList();
    }

    /// <summary>
    /// Syncs the UI entries with the replicated LobbyNetwork player list.
    /// Adds entries for new players, removes entries for disconnected players, and updates usernames.
    /// </summary>
    private void RefreshPlayerList()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return;

        SubscribeToLobbyNetwork();

        if (subscribedLobbyNetwork == null || !subscribedLobbyNetwork.IsSpawned)
        {
            ClearPlayerEntries();
            if (playerCountText != null)
            {
                playerCountText.text = "Connecting...";
            }
            return;
        }

        Dictionary<ulong, string> currentPlayers = new Dictionary<ulong, string>();
        foreach (LobbyPlayerInfo info in subscribedLobbyNetwork.Players)
        {
            currentPlayers[info.ClientId] = info.Username.ToString();
        }

        // Remove entries for players no longer connected
        var toRemove = new List<ulong>();
        foreach (ulong existingId in playerEntries.Keys)
        {
            if (!currentPlayers.ContainsKey(existingId))
            {
                toRemove.Add(existingId);
            }
        }
        foreach (ulong id in toRemove)
        {
            RemovePlayerEntry(id);
        }

        // Add entries for new players
        foreach (ulong id in currentPlayers.Keys)
        {
            if (!playerEntries.ContainsKey(id))
            {
                AddPlayerEntry(id);
            }
        }

        RefreshPlayerNumbers(currentPlayers);
        UpdatePlayerCount();
    }

    private void AddPlayerEntry(ulong clientId)
    {
        if (playerEntries.ContainsKey(clientId))
            return;

        if (playerEntryPrefab == null || contentParent == null)
        {
            Debug.LogError("[LobbyUI] Cannot add entry — prefab or content parent not assigned!");
            return;
        }

        GameObject entry = Instantiate(playerEntryPrefab, contentParent);
        entry.name = $"PlayerEntry_{clientId}";

        playerEntries[clientId] = entry;
        Debug.Log($"[LobbyUI] Added entry for Client {clientId}. Total: {playerEntries.Count}");
    }

    private void RemovePlayerEntry(ulong clientId)
    {
        if (playerEntries.TryGetValue(clientId, out GameObject entry))
        {
            if (entry != null)
            {
                entry.SetActive(false);
                Destroy(entry);
            }
            playerEntries.Remove(clientId);

            Debug.Log($"[LobbyUI] Removed entry for Client {clientId}. Total: {playerEntries.Count}");
        }
    }

    private void ClearPlayerEntries()
    {
        foreach (var entry in playerEntries.Values)
        {
            if (entry != null)
            {
                entry.SetActive(false);
                Destroy(entry);
            }
        }
        playerEntries.Clear();
    }

    /// <summary>
    /// Re-numbers all entries with synchronized usernames: "1. Username (Host)", "2. Username", etc.
    /// Falls back to "Player N" if a client's username hasn't arrived yet.
    /// </summary>
    private void RefreshPlayerNumbers(Dictionary<ulong, string> currentPlayers)
    {
        int playerNumber = 1;

        var sortedIds = new List<ulong>(playerEntries.Keys);
        sortedIds.Sort();

        foreach (ulong clientId in sortedIds)
        {
            if (playerEntries.TryGetValue(clientId, out GameObject entry) && entry != null)
            {
                Text entryText = entry.GetComponentInChildren<Text>();
                if (entryText != null)
                {
                    currentPlayers.TryGetValue(clientId, out string username);
                    if (string.IsNullOrEmpty(username))
                        username = $"Player {playerNumber}";

                    string hostLabel = (clientId == NetworkManager.ServerClientId) ? " (Host)" : "";
                    entryText.text = $"{playerNumber}. {username}{hostLabel}";
                }

                entry.transform.SetSiblingIndex(playerNumber - 1);
                playerNumber++;
            }
        }
    }

    private void UpdatePlayerCount()
    {
        if (playerCountText != null)
        {
            playerCountText.text = $"Players: {playerEntries.Count}/{maxPlayers}";
        }
    }

    public void OnStartGameClicked()
    {
        if (isStartingGame) return;

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[LobbyUI] NetworkManager is null!");
            return;
        }

        if (!NetworkManager.Singleton.IsHost && !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[LobbyUI] Only the host can start the game.");
            return;
        }

        isStartingGame = true;
        if (startGameButton != null)
        {
            startGameButton.interactable = false;
        }

        Debug.Log($"[LobbyUI] Host starting game. Loading '{gameSceneName}'...");

        // LoadScene via NetworkManager syncs all connected clients to the new scene
        NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
    }

    private void OnDisable()
    {
        isInitialized = false;
        UnsubscribeFromLobbyNetwork();
        ClearPlayerEntries();

        if (startGameButton != null && startGameButton.onClick.GetPersistentEventCount() == 0)
        {
            startGameButton.onClick.RemoveAllListeners();
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromLobbyNetwork();
    }
}
