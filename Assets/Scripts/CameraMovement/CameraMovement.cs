using UnityEngine;
using Unity.Netcode;
using Unity.Cinemachine;
using System.Collections;

/// <summary>
/// Attaches to the PLAYER PREFAB ONLY. On spawn, the local owner finds the scene's
/// CinemachineCamera and assigns Follow/LookAt to this player's transform.
///
/// Uses a delayed coroutine-based initialization to avoid a Cinemachine timing issue
/// where the CinemachineFollow component captures a stale reference frame when the
/// target is assigned before the player's spawn position has settled.
///
/// WHY THIS IS NEEDED:
/// CinemachineFollow with "Lock To Target With World Up" binding mode captures its
/// internal reference frame (the coordinate system for Follow Offset) at the moment
/// it first detects a target. In WebGL builds with Relay, OnNetworkSpawn() fires
/// before the player transform reaches its final spawn position — the player may
/// still be at origin (0,0,0) or mid-teleport from SpawnManager. Cinemachine locks
/// to that stale frame and never recalculates, causing the camera to point at the
/// wrong location. Manually toggling Binding Mode in the Inspector forces a
/// recalculation, which is why that workaround works.
///
/// The fix: wait for the spawn position to settle, then assign targets + force
/// Cinemachine to snap its internal state to the correct position.
///
/// SETUP:
/// 1. Place a CinemachineCamera in the Game scene (NOT on the player prefab).
/// 2. Attach this script to the root of the player prefab (same GameObject as NetworkObject).
/// 3. No tags or Inspector drag-and-drop needed — resolution is fully automatic.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]  // Prevents attaching to non-player objects
public class CameraMovement : NetworkBehaviour
{
    [Header("Speed-based FOV")]
    public float minFOV = 60f;
    public float maxFOV = 80f;
    public float maxSpeed = 20f;

    [Header("Initialization")]
    [Tooltip("Number of frames to wait after spawn before assigning the camera. " +
             "Gives the player transform time to reach its final spawn position.")]
    public int framesToWait = 3;

    // Resolved at runtime — never serialized
    private CinemachineCamera vcam;
    private PlayerMovement playerMovement;
    private bool isNetworked = false;
    private bool cameraAssigned = false;

    public override void OnNetworkSpawn()
    {
        isNetworked = true;
        playerMovement = GetComponent<PlayerMovement>();

        // Safety: if this script is somehow on a non-player object, bail out
        if (playerMovement == null)
        {
            Debug.LogError($"[CameraMovement] This script is on '{gameObject.name}' which has no PlayerMovement. "
                         + "It must only be on the player prefab. Disabling.");
            enabled = false;
            return;
        }

        if (!IsOwner)
        {
            // Non-owners must never touch the camera
            enabled = false;
            return;
        }

        // Start the delayed camera assignment coroutine instead of assigning immediately.
        // This ensures the player transform has settled at its final spawn position
        // before Cinemachine captures its reference frame.
        StartCoroutine(DelayedCameraAssignment());
    }

    private void Start()
    {
        // Offline mode: if no network session is running, assign camera immediately
        // (no Relay/network timing issues in offline mode)
        if (!isNetworked)
        {
            StartCoroutine(DelayedCameraAssignment());
        }
    }

    /// <summary>
    /// Waits for the scene and player position to fully settle, then finds the
    /// CinemachineCamera and assigns targets. Retries every frame if the vcam
    /// isn't available yet (e.g., scene still loading after network transition).
    /// </summary>
    private IEnumerator DelayedCameraAssignment()
    {
        // Wait a few frames for:
        // 1. SpawnManager to teleport the player to its spawn position
        // 2. Rigidbody physics to settle
        // 3. The scene's CinemachineCamera to initialize
        for (int i = 0; i < framesToWait; i++)
        {
            yield return null;
        }

        Debug.Log($"[CameraMovement] Delayed init started after {framesToWait} frames. " +
                  $"Player position: {transform.position}");

        // Keep retrying until the CinemachineCamera is found
        while (!AssignCamera())
        {
            yield return null;
        }
    }

    /// <summary>
    /// Finds the scene's CinemachineCamera, assigns Follow + LookAt, and forces
    /// Cinemachine to snap to the correct position.
    /// Returns false if the vcam isn't available yet (caller should retry).
    /// </summary>
    private bool AssignCamera()
    {
        if (cameraAssigned) return true;

        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>();

        vcam = FindAnyObjectByType<CinemachineCamera>();

        if (vcam == null)
        {
            Debug.Log("[CameraMovement] CinemachineCamera not found yet. Will retry...");
            return false;
        }

        // Assign tracking targets
        vcam.Follow = transform;
        vcam.LookAt = transform;

        // Force Cinemachine to snap its internal state to the correct position.
        // This resets the CinemachineFollow reference frame so "Lock To Target With
        // World Up" calculates its offset from the player's CURRENT position, not
        // from wherever the player was when the target was first detected.
        //
        // We compute the expected camera position from the player's current position
        // plus the Follow Offset that's configured on the CinemachineFollow component.
        // This matches what the camera SHOULD be seeing.
        Vector3 followOffset = new Vector3(0f, 5f, -7f); // Match Inspector Follow Offset
        Vector3 desiredPosition = transform.position + transform.rotation * followOffset;
        Quaternion desiredRotation = Quaternion.LookRotation(transform.position - desiredPosition, Vector3.up);

        vcam.ForceCameraPosition(desiredPosition, desiredRotation);

        cameraAssigned = true;

        Debug.Log($"[CameraMovement] Cinemachine vcam '{vcam.gameObject.name}' now following "
                + $"local player (Client {OwnerClientId}). "
                + $"Follow={vcam.Follow.name}, LookAt={vcam.LookAt.name}. "
                + $"Forced position: {desiredPosition}");
        return true;
    }

    private void Update()
    {
        if (isNetworked && !IsOwner) return;

        // Don't do FOV updates until camera is assigned
        if (!cameraAssigned || vcam == null || playerMovement == null) return;

        // Speed-based FOV effect
        float speed = playerMovement.CurrentSpeed;
        float t = maxSpeed > 0f ? Mathf.Clamp01(speed / maxSpeed) : 0f;
        vcam.Lens.FieldOfView = Mathf.Lerp(minFOV, maxFOV, t);
    }
}
