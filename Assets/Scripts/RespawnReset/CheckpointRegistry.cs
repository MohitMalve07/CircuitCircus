using UnityEngine;

/// <summary>
/// Scene-placed registry that stores all checkpoint transforms by index.
/// Used by RespawnSystem to look up checkpoint positions for respawning
/// without needing to sync Vector3/Quaternion over the network.
/// 
/// SETUP:
/// 1. Create an empty GameObject in the Game scene named "CheckpointRegistry".
/// 2. Attach this script.
/// 3. Drag all checkpoint GameObjects into the 'checkpoints' array IN ORDER (index 0 = finish line).
/// </summary>
public class CheckpointRegistry : MonoBehaviour
{
    [Tooltip("All checkpoint GameObjects in order. Index 0 = finish/start line, 1..N = track checkpoints.")]
    public Transform[] checkpoints;

    /// <summary>
    /// Singleton-style access. There should be exactly one in the scene.
    /// </summary>
    public static CheckpointRegistry Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CheckpointRegistry] Duplicate found — destroying this one.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Returns the position and rotation for a given checkpoint index.
    /// Returns false if the index is out of range.
    /// </summary>
    public bool TryGetCheckpoint(int index, out Vector3 position, out Quaternion rotation)
    {
        if (checkpoints == null || index < 0 || index >= checkpoints.Length || checkpoints[index] == null)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }

        position = checkpoints[index].position;
        rotation = checkpoints[index].rotation;
        return true;
    }

    /// <summary>
    /// Total number of checkpoints (including the finish line at index 0).
    /// </summary>
    public int TotalCheckpoints => checkpoints != null ? checkpoints.Length : 0;

    /// <summary>
    /// Number of mid-track checkpoints (excludes the finish line at index 0).
    /// </summary>
    public int TrackCheckpointCount => Mathf.Max(0, TotalCheckpoints - 1);

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
