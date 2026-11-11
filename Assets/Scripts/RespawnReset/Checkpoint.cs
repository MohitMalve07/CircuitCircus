using UnityEngine;

/// <summary>
/// Detects when a player enters a checkpoint trigger and delegates to RaceProgress.
/// This is a plain MonoBehaviour on scene checkpoint GameObjects — no networking needed.
/// 
/// SETUP:
/// 1. Add this script to each checkpoint and the finish line.
/// 2. Ensure each has a Collider set to "Is Trigger".
/// 3. Ensure the player root has the "Player" tag and a RaceProgress component.
/// 4. Make sure a CheckpointRegistry exists in the scene with all checkpoints in order
///    (index 0 = finish line). The checkpointIndex is auto-assigned from the registry.
/// 
/// NOTE: checkpointIndex is assigned automatically at runtime from CheckpointRegistry.
///       You do NOT need to set it manually in the Inspector.
/// </summary>
public class Checkpoint : MonoBehaviour
{
    [Tooltip("Auto-assigned at runtime from CheckpointRegistry. Do not set manually.")]
    [HideInInspector]
    public int checkpointIndex = -1;

    private void Start()
    {
        // Auto-assign checkpointIndex by finding this transform in the CheckpointRegistry
        if (CheckpointRegistry.Instance != null)
        {
            Transform[] checkpoints = CheckpointRegistry.Instance.checkpoints;
            for (int i = 0; i < checkpoints.Length; i++)
            {
                if (checkpoints[i] == transform)
                {
                    checkpointIndex = i;
                    Debug.Log($"[Checkpoint] '{gameObject.name}' auto-assigned index {i} from CheckpointRegistry.");
                    return;
                }
            }

            Debug.LogError($"[Checkpoint] '{gameObject.name}' is NOT in the CheckpointRegistry! "
                         + "Add its Transform to the registry's checkpoints array.");
        }
        else
        {
            Debug.LogError("[Checkpoint] No CheckpointRegistry found in scene!");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Find the root GameObject (where the tag, NetworkObject, and scripts live)
        GameObject root = other.attachedRigidbody != null
            ? other.attachedRigidbody.gameObject
            : other.gameObject;

        if (!root.CompareTag("Player"))
        {
            return;
        }

        if (checkpointIndex < 0)
        {
            Debug.LogError($"[Checkpoint] '{gameObject.name}' has no valid index. Is it in the CheckpointRegistry?");
            return;
        }

        // Delegate to RaceProgress for server-authoritative validation
        RaceProgress raceProgress = root.GetComponent<RaceProgress>();
        if (raceProgress != null)
        {
            raceProgress.NotifyCheckpointHit(checkpointIndex);
            Debug.Log($"[Checkpoint] Player hit checkpoint {checkpointIndex} ({gameObject.name})");
        }
        else
        {
            Debug.LogWarning($"[Checkpoint] RaceProgress not found on {root.name}. "
                           + "Is the RaceProgress script attached to the player prefab?");
        }
    }
}
