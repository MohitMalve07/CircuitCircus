using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Drives a speedometer needle in the scene UI based on the local player's speed.
/// 
/// This script should be placed on the speedometer UI object in the SCENE (not on
/// the player prefab). It dynamically finds the local player's PlayerMovement at
/// runtime — no Inspector drag-and-drop needed.
/// 
/// SETUP:
/// 1. Attach to the speedometer GameObject in the Canvas.
/// 2. Assign the needle Transform in the Inspector.
/// 3. No player reference needed — it's found automatically.
/// </summary>
public class Speedometer : MonoBehaviour
{
    [Header("Needle Reference")]
    [Tooltip("The transform of the speedometer needle that will rotate.")]
    [SerializeField] private Transform needleTransform;

    [Header("Settings")]
    [Tooltip("The speed value that corresponds to the maximum rotation.")]
    [SerializeField] private float maxSpeed = 15f;
    
    [Tooltip("The Z rotation angle at zero speed.")]
    [SerializeField] private float minRotationAngle = 180f;
    
    [Tooltip("The Z rotation angle at maximum speed.")]
    [SerializeField] private float maxRotationAngle = -95f;

    [Tooltip("How smoothly the needle moves towards the target speed.")]
    [SerializeField] private float smoothing = 10f;

    // Dynamically found at runtime
    private PlayerMovement localPlayerMovement;

    private void Update()
    {
        // Keep trying to find the local player until we have one
        if (localPlayerMovement == null)
        {
            localPlayerMovement = FindLocalPlayerMovement();
            if (localPlayerMovement == null) return;
            Debug.Log($"[Speedometer] Found local player: '{localPlayerMovement.gameObject.name}'");
        }

        if (needleTransform == null) return;

        // Read speed from local player's movement logic
        float currentSpeed = localPlayerMovement.CurrentSpeed;

        // Normalize speed (0 to 1)
        float normalizedSpeed = Mathf.Clamp01(currentSpeed / maxSpeed);

        // Map to target rotation
        float targetZRotation = Mathf.Lerp(minRotationAngle, maxRotationAngle, normalizedSpeed);

        // Smooth the needle rotation
        float currentZ = needleTransform.localEulerAngles.z;
        float smoothedZ = Mathf.LerpAngle(currentZ, targetZRotation, Time.deltaTime * smoothing);

        needleTransform.localRotation = Quaternion.Euler(0, 0, smoothedZ);
    }

    /// <summary>
    /// Finds the local player's PlayerMovement component.
    /// In networked mode, finds the owner's instance.
    /// In offline mode, finds any PlayerMovement.
    /// </summary>
    private PlayerMovement FindLocalPlayerMovement()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            // Networked: find the local client's player object
            if (NetworkManager.Singleton.LocalClient != null &&
                NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                return NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerMovement>();
            }
            return null;
        }

        // Offline: find any PlayerMovement in the scene
        return FindAnyObjectByType<PlayerMovement>();
    }
}
