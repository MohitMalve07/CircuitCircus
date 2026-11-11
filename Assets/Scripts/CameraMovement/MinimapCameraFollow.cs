using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Attaches to the PLAYER PREFAB ONLY. On spawn, the local owner finds the scene's
/// minimap camera and makes it follow this player from a fixed height.
/// 
/// IMPORTANT: Do NOT attach this to any scene camera.
///            This script requires a PlayerMovement component on the same GameObject.
/// 
/// SETUP:
/// 1. Place a Camera in the Game scene for the minimap (NOT on the player prefab).
///    Name the GameObject "MinimapCamera".
/// 2. Attach this script to the root of the player prefab.
/// 3. No Inspector drag-and-drop needed.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]  // Prevents attaching to non-player objects
public class MinimapCameraFollow : NetworkBehaviour
{
    [Header("Minimap Settings")]
    public float height = 50f;

    [Tooltip("Name of the minimap camera GameObject in the scene.")]
    public string minimapCameraName = "MinimapCamera";

    // Resolved at runtime
    private Camera minimapCam;
    private bool isNetworked = false;
    private bool cameraAssigned = false;

    public override void OnNetworkSpawn()
    {
        isNetworked = true;

        if (GetComponent<PlayerMovement>() == null)
        {
            Debug.LogError($"[MinimapCameraFollow] This script is on '{gameObject.name}' which has no PlayerMovement. "
                         + "It must only be on the player prefab. Disabling.");
            enabled = false;
            return;
        }

        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        AssignMinimapCamera();
    }

    private void Start()
    {
        // Offline mode: if no network session is running, assign immediately
        if (!isNetworked)
        {
            AssignMinimapCamera();
        }
    }

    /// <summary>
    /// Finds the minimap camera by GameObject name and caches it.
    /// </summary>
    private void AssignMinimapCamera()
    {
        if (cameraAssigned) return;

        GameObject camObj = GameObject.Find(minimapCameraName);
        if (camObj != null)
        {
            minimapCam = camObj.GetComponent<Camera>();
        }

        if (minimapCam == null)
        {
            // Scene may still be loading — don't disable, retry later
            return;
        }

        cameraAssigned = true;
        Debug.Log($"[MinimapCameraFollow] Minimap camera '{minimapCam.gameObject.name}' now following "
                + $"local player (Client {OwnerClientId}).");
    }

    private void LateUpdate()
    {
        if (isNetworked && !IsOwner) return;

        // Keep retrying until the minimap camera is found
        if (!cameraAssigned)
        {
            AssignMinimapCamera();
            return;
        }

        if (minimapCam == null) return;

        minimapCam.transform.position = new Vector3(
            transform.position.x,
            height,
            transform.position.z
        );
    }
}