using UnityEngine;
using Unity.Netcode;
using Unity.Collections;

/// <summary>
/// Synchronizes a player's chosen username across the network.
/// Attach to the player prefab alongside PlayerMovement, RaceProgress, etc.
///
/// Flow:
/// 1. Player types a name in the main menu InputField → stored in NetworkUI.LocalUsername.
/// 2. Player prefab spawns → OnNetworkSpawn fires on the owner.
/// 3. Owner sends the local username to the server via SetUsernameServerRpc.
/// 4. Server writes the NetworkVariable → value syncs to all clients automatically.
///
/// Other scripts can call NetworkPlayer.GetUsername(clientId) to look up any
/// player's display name at any time.
/// </summary>
public class NetworkPlayer : NetworkBehaviour
{
    /// <summary>
    /// The synchronized username. Server-writable, readable by everyone.
    /// FixedString32Bytes supports up to 29 UTF-8 characters.
    /// </summary>
    public NetworkVariable<FixedString32Bytes> Username = new NetworkVariable<FixedString32Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            // Read the username that was captured in the main menu
            string localName = NetworkUI.LocalUsername;

            if (string.IsNullOrWhiteSpace(localName))
            {
                localName = "Racer" + Random.Range(100, 999);
            }

            Debug.Log($"[NetworkPlayer] Owner (Client {OwnerClientId}) sending username: '{localName}'");
            SetUsernameServerRpc(localName);
        }
    }

    /// <summary>
    /// Owner → Server: sets the username NetworkVariable.
    /// </summary>
    [ServerRpc]
    private void SetUsernameServerRpc(string username, ServerRpcParams rpcParams = default)
    {
        // Truncate if somehow too long for FixedString32Bytes
        if (username.Length > 29)
            username = username.Substring(0, 29);

        Username.Value = new FixedString32Bytes(username);
        Debug.Log($"[NetworkPlayer] Server set username for Client {OwnerClientId}: '{username}'");
    }

    /// <summary>
    /// Static helper to look up any player's username by their client ID.
    /// Searches all spawned NetworkPlayer instances.
    /// Returns the username string, or empty string if not found.
    /// </summary>
    public static string GetUsername(ulong clientId)
    {
        // Try the fast path: look up via NetworkManager's connected clients
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost)
            {
                // Server/Host can access ConnectedClients directly
                if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
                {
                    if (client.PlayerObject != null)
                    {
                        NetworkPlayer np = client.PlayerObject.GetComponent<NetworkPlayer>();
                        if (np != null)
                        {
                            return np.Username.Value.ToString();
                        }
                    }
                }
            }
            else
            {
                // Client: search spawned NetworkObjects
                foreach (var networkObj in Object.FindObjectsByType<NetworkObject>())
                {
                    if (networkObj.IsPlayerObject && networkObj.OwnerClientId == clientId)
                    {
                        NetworkPlayer np = networkObj.GetComponent<NetworkPlayer>();
                        if (np != null)
                        {
                            return np.Username.Value.ToString();
                        }
                    }
                }
            }
        }

        return "";
    }
}
