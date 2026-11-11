using System.Collections;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Handles player respawning at the last server-validated checkpoint.
/// Reads checkpoint progress from RaceProgress (NetworkVariable) and
/// looks up positions from CheckpointRegistry (scene singleton).
/// 
/// Attach to the player prefab alongside PlayerMovement and RaceProgress.
/// </summary>
public class RespawnSystem : NetworkBehaviour
{
    private Rigidbody rb;
    private PlayerMovement playerMovement;
    private RaceProgress raceProgress;

    private Vector3 initialSpawnPos;
    private Quaternion initialSpawnRot;
    private bool hasInitialSpawn = false;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        playerMovement = GetComponent<PlayerMovement>();
        raceProgress = GetComponent<RaceProgress>();

        initialSpawnPos = transform.position;
        initialSpawnRot = transform.rotation;
        hasInitialSpawn = true;
        StartCoroutine(CaptureSettledSpawnPosition());

        Debug.Log("[RespawnSystem] Initialized — using server-validated checkpoints via RaceProgress.");
    }

    private IEnumerator CaptureSettledSpawnPosition()
    {
        // Wait a few frames so initial network transform sync settles on clients before input unlocks
        for (int i = 0; i < 3; i++)
        {
            yield return null;
        }

        initialSpawnPos = transform.position;
        initialSpawnRot = transform.rotation;
        hasInitialSpawn = true;
    }

    void Update()
    {
        // Only the owning client (or offline player) should process respawn input
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (!IsOwner) return;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            Respawn();
        }
    }

    public void Respawn()
    {
        // Determine the respawn checkpoint from server-validated progress
        int checkpointIndex = 0;
        if (raceProgress != null)
        {
            checkpointIndex = raceProgress.CurrentCheckpointIndex.Value;
        }

        // Look up the position from the scene registry
        Vector3 respawnPos;
        Quaternion respawnRot;

        if (CheckpointRegistry.Instance != null &&
            CheckpointRegistry.Instance.TryGetCheckpoint(checkpointIndex, out respawnPos, out respawnRot))
        {
            ApplyRespawn(respawnPos, respawnRot, checkpointIndex);
        }
        else if (checkpointIndex > 0 &&
                 CheckpointRegistry.Instance != null &&
                 CheckpointRegistry.Instance.TryGetCheckpoint(0, out respawnPos, out respawnRot))
        {
            Debug.LogWarning($"[RespawnSystem] Checkpoint {checkpointIndex} missing — falling back to checkpoint 0.");
            ApplyRespawn(respawnPos, respawnRot, 0);
        }
        else if (hasInitialSpawn)
        {
            Debug.LogWarning($"[RespawnSystem] CheckpointRegistry missing or checkpoint {checkpointIndex} unavailable — falling back to initial spawn position.");
            ApplyRespawn(initialSpawnPos, initialSpawnRot, 0);
        }
        else
        {
            Debug.LogError($"[RespawnSystem] Cannot respawn — checkpoint {checkpointIndex} and initial spawn unavailable.");
        }
    }

    private void ApplyRespawn(Vector3 position, Quaternion rotation, int checkpointIndex)
    {
        // 1. Zero out all physics velocity
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // 2. Zero out the custom moveForce in PlayerMovement
        if (playerMovement != null)
        {
            playerMovement.ResetMoveForce();
        }

        // 3. Teleport using Rigidbody for physics-safe repositioning
        Vector3 spawnPos = position + Vector3.up * 1f;
        rb.position = spawnPos;
        rb.rotation = rotation;

        // Also sync transform immediately (avoids one-frame desync)
        transform.position = spawnPos;
        transform.rotation = rotation;

        Debug.Log($"[RespawnSystem] Respawned at checkpoint {checkpointIndex} pos: {position}");
    }
}
