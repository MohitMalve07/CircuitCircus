using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Serializable lobby entry replicated to all connected clients via <see cref="NetworkList{T}"/>.
/// </summary>
public struct LobbyPlayerInfo : INetworkSerializable, IEquatable<LobbyPlayerInfo>
{
    public ulong ClientId;
    public FixedString32Bytes Username;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref Username);
    }

    public bool Equals(LobbyPlayerInfo other)
    {
        return ClientId == other.ClientId && Username.Equals(other.Username);
    }
}

/// <summary>
/// Server-authoritative lobby player roster synced across Host and Clients in the Main Menu scene.
/// Attach to an active root GameObject with a <see cref="NetworkObject"/> component.
/// </summary>
public class LobbyNetwork : NetworkBehaviour
{
    public static LobbyNetwork Instance { get; private set; }

    private readonly NetworkList<LobbyPlayerInfo> players = new NetworkList<LobbyPlayerInfo>();

    public NetworkList<LobbyPlayerInfo> Players => players;

    public event Action OnLobbyPlayersChanged;

    private bool callbacksSubscribed = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[LobbyNetwork] Duplicate instance found — destroying this one.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        players.OnListChanged += HandlePlayersListChanged;

        if (IsServer && NetworkManager.Singleton != null)
        {
            players.Clear();
            SubscribeServerCallbacks();

            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                FixedString32Bytes initialName = (clientId == NetworkManager.ServerClientId)
                    ? ToFixedString32(NetworkUI.LocalUsername)
                    : default;

                UpsertPlayer(clientId, initialName);
            }
        }
        else if (IsClient)
        {
            SubmitUsernameRpc(NetworkUI.LocalUsername);
        }

        OnLobbyPlayersChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        players.OnListChanged -= HandlePlayersListChanged;
        UnsubscribeServerCallbacks();
        OnLobbyPlayersChanged?.Invoke();
    }

    public override void OnDestroy()
    {
        players.OnListChanged -= HandlePlayersListChanged;
        UnsubscribeServerCallbacks();

        if (Instance == this)
        {
            Instance = null;
        }

        base.OnDestroy();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SubmitUsernameRpc(string rawUsername, RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        FixedString32Bytes sanitized = ToFixedString32(rawUsername);

        UpsertPlayer(senderId, sanitized);
        Debug.Log($"[LobbyNetwork] Registered lobby username for Client {senderId}: '{sanitized}'");
    }

    private void SubscribeServerCallbacks()
    {
        if (callbacksSubscribed || NetworkManager.Singleton == null) return;

        NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        callbacksSubscribed = true;
    }

    private void UnsubscribeServerCallbacks()
    {
        if (!callbacksSubscribed) return;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        callbacksSubscribed = false;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (!IsServer) return;

        // Avoid overwriting a name if the client already submitted via RPC.
        if (FindPlayerIndex(clientId) >= 0) return;

        FixedString32Bytes initialName = (clientId == NetworkManager.ServerClientId)
            ? ToFixedString32(NetworkUI.LocalUsername)
            : default;

        players.Add(new LobbyPlayerInfo
        {
            ClientId = clientId,
            Username = initialName
        });
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;

        int index = FindPlayerIndex(clientId);
        if (index >= 0)
        {
            players.RemoveAt(index);
        }
    }

    private void HandlePlayersListChanged(NetworkListEvent<LobbyPlayerInfo> changeEvent)
    {
        OnLobbyPlayersChanged?.Invoke();
    }

    private void UpsertPlayer(ulong clientId, FixedString32Bytes username)
    {
        var entry = new LobbyPlayerInfo
        {
            ClientId = clientId,
            Username = username
        };

        int index = FindPlayerIndex(clientId);
        if (index >= 0)
        {
            players[index] = entry;
        }
        else
        {
            players.Add(entry);
        }
    }

    private int FindPlayerIndex(ulong clientId)
    {
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].ClientId == clientId)
            {
                return i;
            }
        }

        return -1;
    }

    private static FixedString32Bytes ToFixedString32(string value)
    {
        string sanitized = NetworkUI.SanitizeUsername(value);
        return string.IsNullOrEmpty(sanitized) ? default : new FixedString32Bytes(sanitized);
    }
}
