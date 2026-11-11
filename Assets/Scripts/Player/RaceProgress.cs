using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Server-authoritative checkpoint and lap tracking for a single player.
/// Attach to the player prefab alongside PlayerMovement and NetworkObject.
/// 
/// Flow:
/// 1. Client hits a checkpoint trigger → Checkpoint.cs calls NotifyCheckpointHit()
/// 2. Only the owner sends ReportCheckpointServerRpc() to the server
/// 3. Server validates sequential order and updates NetworkVariables
/// 4. All clients see updated state via NetworkVariable sync
/// 
/// Checkpoint indexing:
/// - Index 0 = finish/start line
/// - Index 1..N = mid-track checkpoints
/// - A lap completes when the player hits checkpoint 0 after passing all 1..N checkpoints
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class RaceProgress : NetworkBehaviour
{
    [Header("Race Settings")]
    [Tooltip("Number of mid-track checkpoints (excluding the finish line). Must match CheckpointRegistry.")]
    public int totalTrackCheckpoints = 3;

    [Tooltip("Total laps needed to finish the race.")]
    public int totalLaps = 3;

    /// <summary>
    /// The last validated mid-track checkpoint index this player passed (1..N).
    /// Reset to 0 after completing a lap. Server-written, all clients read.
    /// </summary>
    public NetworkVariable<int> CurrentCheckpointIndex = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>
    /// Current completed lap count. Starts at 0, increments when a lap is completed.
    /// Server-written, all clients read.
    /// </summary>
    public NetworkVariable<int> CurrentLap = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>
    /// True when this player has finished the race (completed all laps).
    /// </summary>
    public NetworkVariable<bool> RaceFinished = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>
    /// Fired locally when the CurrentLap NetworkVariable changes.
    /// LapTimer and UI can subscribe to this.
    /// </summary>
    public event System.Action<int> OnLapChanged;

    /// <summary>
    /// Fired locally when the race is finished.
    /// </summary>
    public event System.Action OnRaceFinished;

    // Track whether the player has crossed the start line at least once
    private bool raceStarted = false;

    public override void OnNetworkSpawn()
    {
        // Auto-read checkpoint count from registry (server-side and client-side)
        AutoConfigureFromRegistry();

        // Subscribe to NetworkVariable changes for local events
        CurrentLap.OnValueChanged += HandleLapChanged;
        RaceFinished.OnValueChanged += HandleRaceFinished;
    }

    private void Start()
    {
        // Offline mode: also auto-configure
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            AutoConfigureFromRegistry();
        }
    }

    /// <summary>
    /// Reads the checkpoint count from CheckpointRegistry so it can't go out of sync.
    /// Registry has N entries: index 0 = finish line, 1..N-1 = mid-track checkpoints.
    /// </summary>
    private void AutoConfigureFromRegistry()
    {
        if (CheckpointRegistry.Instance != null)
        {
            totalTrackCheckpoints = CheckpointRegistry.Instance.TrackCheckpointCount;
            Debug.Log($"[RaceProgress] Auto-configured: {totalTrackCheckpoints} mid-track checkpoints from registry.");
        }
        else
        {
            Debug.LogWarning("[RaceProgress] CheckpointRegistry not found — using Inspector value for totalTrackCheckpoints.");
        }
    }

    public override void OnNetworkDespawn()
    {
        CurrentLap.OnValueChanged -= HandleLapChanged;
        RaceFinished.OnValueChanged -= HandleRaceFinished;
    }

    private void HandleLapChanged(int previousValue, int newValue)
    {
        Debug.Log($"[RaceProgress] Client {OwnerClientId} lap changed: {previousValue} → {newValue}");
        OnLapChanged?.Invoke(newValue);
    }

    private void HandleRaceFinished(bool previousValue, bool newValue)
    {
        if (newValue)
        {
            Debug.Log($"[RaceProgress] Client {OwnerClientId} has FINISHED the race!");
            OnRaceFinished?.Invoke();
        }
    }

    /// <summary>
    /// Called by Checkpoint.OnTriggerEnter on the client that hit the checkpoint.
    /// Only the owner sends the RPC to the server.
    /// </summary>
    public void NotifyCheckpointHit(int checkpointIndex)
    {
        // Only the owner should report to the server
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (!IsOwner) return;
            ReportCheckpointServerRpc(checkpointIndex);
        }
        else
        {
            // Offline mode: process locally for testing
            ProcessCheckpoint(checkpointIndex);
        }
    }

    /// <summary>
    /// Owner → Server: "I hit checkpoint X". Server validates and updates state.
    /// </summary>
    [ServerRpc]
    private void ReportCheckpointServerRpc(int checkpointIndex, ServerRpcParams rpcParams = default)
    {
        ProcessCheckpoint(checkpointIndex);
    }

    /// <summary>
    /// Server-side checkpoint validation and state update.
    /// Also used directly in offline mode.
    /// </summary>
    private void ProcessCheckpoint(int checkpointIndex)
    {
        // Don't process anything if the race is already finished
        if (RaceFinished.Value) return;

        // Checkpoint 0 = finish/start line
        if (checkpointIndex == 0)
        {
            HandleFinishLine();
            return;
        }

        // Mid-track checkpoint (1..N): must be sequential
        int expectedIndex = CurrentCheckpointIndex.Value + 1;

        if (checkpointIndex == expectedIndex)
        {
            CurrentCheckpointIndex.Value = checkpointIndex;
            Debug.Log($"[RaceProgress] Server validated checkpoint {checkpointIndex} for Client {OwnerClientId}. "
                    + $"Progress: {checkpointIndex}/{totalTrackCheckpoints}");
        }
        else if (checkpointIndex <= CurrentCheckpointIndex.Value)
        {
            // Already passed this one — ignore silently
            Debug.Log($"[RaceProgress] Checkpoint {checkpointIndex} ignored for Client {OwnerClientId} "
                    + $"(already at {CurrentCheckpointIndex.Value}).");
        }
        else
        {
            Debug.LogWarning($"[RaceProgress] Checkpoint {checkpointIndex} REJECTED for Client {OwnerClientId}. "
                           + $"Expected {expectedIndex}. Possible skip attempt.");
        }
    }

    /// <summary>
    /// Handles the finish/start line crossing (checkpoint 0).
    /// </summary>
    private void HandleFinishLine()
    {
        if (!raceStarted)
        {
            // First crossing: race starts, no lap counted
            raceStarted = true;
            CurrentCheckpointIndex.Value = 0;
            CurrentLap.Value = 1;
            Debug.Log($"[RaceProgress] Race started for Client {OwnerClientId}.");
            return;
        }

        // Subsequent crossings: check if all mid-track checkpoints were passed
        if (CurrentCheckpointIndex.Value >= totalTrackCheckpoints)
        {
            // Valid lap completion
            int newLap = CurrentLap.Value + 1;
            CurrentCheckpointIndex.Value = 0; // Reset checkpoint progress

            if (newLap > totalLaps)
            {
                // Race finished
                RaceFinished.Value = true;
                Debug.Log($"[RaceProgress] Client {OwnerClientId} FINISHED the race in {totalLaps} laps!");

                // Notify the RaceManager (server-side) to track finish order
                if (RaceManager.Instance != null)
                {
                    RaceManager.Instance.OnPlayerFinished(OwnerClientId);
                }
            }
            else
            {
                CurrentLap.Value = newLap;
                Debug.Log($"[RaceProgress] Client {OwnerClientId} completed lap {newLap - 1}. Now on lap {newLap}/{totalLaps}.");
            }
        }
        else
        {
            Debug.LogWarning($"[RaceProgress] Finish line crossing REJECTED for Client {OwnerClientId}. "
                           + $"Only passed {CurrentCheckpointIndex.Value}/{totalTrackCheckpoints} checkpoints.");
        }
    }

    /// <summary>
    /// Convenience accessors for UI and other scripts.
    /// </summary>
    public int Laps => CurrentLap.Value;
    public int TotalLapsRequired => totalLaps;
    public bool IsRaceFinished => RaceFinished.Value;
}
