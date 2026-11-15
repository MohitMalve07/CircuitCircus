using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays the player's synchronized username floating above their kart during gameplay.
/// Attach to the root of the Player prefab alongside <see cref="NetworkPlayer"/>.
/// </summary>
[RequireComponent(typeof(NetworkPlayer))]
public class PlayerNameplate : NetworkBehaviour
{
    [Header("UI References")]
    [SerializeField] private Canvas nameplateCanvas;
    [SerializeField] private Text nameText;

    [Header("Display Settings")]
    [Tooltip("Height above the player's root position in world units.")]
    [SerializeField] private float worldHeightOffset = 1.85f;

    [Tooltip("Show the nameplate above the local player's own kart as well as other players.")]
    [SerializeField] private bool showLocalPlayerName = true;

    private NetworkPlayer networkPlayer;
    private Transform nameplateTransform;
    private Camera cachedCamera;
    private bool isNetworked;
    private bool isSubscribed;

    public string DisplayedName => nameText != null ? nameText.text : string.Empty;

    private void Awake()
    {
        networkPlayer = GetComponent<NetworkPlayer>();
        ResolveUIReferences();
    }

    public override void OnNetworkSpawn()
    {
        isNetworked = true;
        ResolveUIReferences();

        bool visible = showLocalPlayerName || !IsOwner;
        SetNameplateVisible(visible);

        if (networkPlayer != null && !isSubscribed)
        {
            networkPlayer.Username.OnValueChanged += HandleUsernameChanged;
            isSubscribed = true;
        }

        RefreshNameFromNetwork();
    }

    private void Start()
    {
        // Offline / standalone Game scene testing fallback
        if (!isNetworked)
        {
            ResolveUIReferences();
            SetNameplateVisible(showLocalPlayerName);

            string offlineName = NetworkUI.HasConfirmedUsername()
                ? NetworkUI.SanitizeUsername(NetworkUI.LocalUsername)
                : "Player";

            ApplyDisplayText(offlineName);
        }
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeUsername();
    }

    public override void OnDestroy()
    {
        UnsubscribeUsername();
        base.OnDestroy();
    }

    private void LateUpdate()
    {
        if (nameplateTransform == null || nameplateCanvas == null || !nameplateCanvas.gameObject.activeSelf)
        {
            return;
        }

        // Keep the nameplate upright directly above the kart regardless of kart pitch/roll
        nameplateTransform.position = transform.position + Vector3.up * worldHeightOffset;

        Camera targetCam = GetTargetCamera();
        if (targetCam != null)
        {
            nameplateTransform.rotation = targetCam.transform.rotation;
        }
    }

    private void HandleUsernameChanged(FixedString32Bytes previousValue, FixedString32Bytes newValue)
    {
        ApplyDisplayText(ResolveDisplayName(newValue.ToString()));
    }

    private void RefreshNameFromNetwork()
    {
        string syncedName = networkPlayer != null ? networkPlayer.Username.Value.ToString() : string.Empty;

        // Before the owner's ServerRpc round-trip completes, avoid flashing a placeholder for the local player
        if (string.IsNullOrWhiteSpace(syncedName) && IsOwner && NetworkUI.HasConfirmedUsername())
        {
            syncedName = NetworkUI.SanitizeUsername(NetworkUI.LocalUsername);
        }

        ApplyDisplayText(ResolveDisplayName(syncedName));
    }

    private string ResolveDisplayName(string rawName)
    {
        if (!string.IsNullOrWhiteSpace(rawName))
        {
            return rawName;
        }

        return isNetworked ? $"Player {OwnerClientId + 1}" : "Player";
    }

    private void ApplyDisplayText(string displayName)
    {
        if (nameText != null)
        {
            nameText.text = displayName;
        }
    }

    private void SetNameplateVisible(bool visible)
    {
        if (nameplateCanvas != null)
        {
            nameplateCanvas.gameObject.SetActive(visible);
        }
    }

    private Camera GetTargetCamera()
    {
        if (cachedCamera == null || !cachedCamera.isActiveAndEnabled)
        {
            cachedCamera = Camera.main;
        }

        return cachedCamera;
    }

    private void ResolveUIReferences()
    {
        if (nameplateCanvas == null)
        {
            nameplateCanvas = GetComponentInChildren<Canvas>(true);
        }

        if (nameplateCanvas != null)
        {
            nameplateTransform = nameplateCanvas.transform;

            if (nameText == null)
            {
                nameText = nameplateCanvas.GetComponentInChildren<Text>(true);
            }
        }
    }

    private void UnsubscribeUsername()
    {
        if (isSubscribed && networkPlayer != null)
        {
            networkPlayer.Username.OnValueChanged -= HandleUsernameChanged;
            isSubscribed = false;
        }
    }
}
