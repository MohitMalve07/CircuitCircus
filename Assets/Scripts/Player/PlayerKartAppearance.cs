using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Synchronizes which Kenney kart model variant (red, orange, green, white) a spawned player uses.
/// Server assigns the slot-based index on spawn; all peers apply the matching meshes and materials
/// onto the existing kart visual hierarchy without breaking wheel transforms or colliders.
/// </summary>
public class PlayerKartAppearance : NetworkBehaviour
{
    private const int UnassignedKartIndex = -1;

    [Header("Kart Variants")]
    [Tooltip("Ordered list of kart model prefabs assigned by player slot (0 = Red, 1 = Orange, 2 = Green, 3 = White).")]
    [SerializeField] private GameObject[] kartModelPrefabs;

    [Tooltip("Child Transform containing the kart body and wheel renderers inside Player.prefab.")]
    [SerializeField] private Transform kartVisualRoot;

    /// <summary>
    /// Server-assigned kart variant index replicated to all clients.
    /// Initialized as -1 until SpawnManager assigns the player's slot.
    /// </summary>
    public NetworkVariable<int> KartIndex = new NetworkVariable<int>(
        UnassignedKartIndex,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private int activeVariantIndex = UnassignedKartIndex;
    private bool isSubscribed;

    public int ActiveKartIndex => activeVariantIndex;
    public string ActiveKartName => kartVisualRoot != null ? kartVisualRoot.name : string.Empty;

    private void Awake()
    {
        ResolveKartVisualRoot();
    }

    public override void OnNetworkSpawn()
    {
        // If spawned outside SpawnManager (e.g., manual spawn), derive a fallback slot from client ID.
        if (IsServer && KartIndex.Value == UnassignedKartIndex)
        {
            int fallbackCount = (kartModelPrefabs != null && kartModelPrefabs.Length > 0)
                ? kartModelPrefabs.Length
                : 1;
            KartIndex.Value = (int)(OwnerClientId % (ulong)fallbackCount);
        }

        if (!isSubscribed)
        {
            KartIndex.OnValueChanged += HandleKartIndexChanged;
            isSubscribed = true;
        }

        ApplyKartVariant(KartIndex.Value);
    }

    private void Start()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            ApplyKartVariant(KartIndex.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeKartIndex();
    }

    public override void OnDestroy()
    {
        UnsubscribeKartIndex();
        base.OnDestroy();
    }

    /// <summary>
    /// Called by SpawnManager on the server before (or right after) spawning the player object.
    /// </summary>
    public void AssignKartIndex(int slotIndex)
    {
        if (IsSpawned && !IsServer) return;

        int normalized = ResolveVariantIndex(slotIndex);
        bool changed = KartIndex.Value != normalized;
        KartIndex.Value = normalized;

        if (!changed || !IsSpawned)
        {
            ApplyKartVariant(normalized);
        }
    }

    private void HandleKartIndexChanged(int previousValue, int newValue)
    {
        ApplyKartVariant(newValue);
    }

    private void ApplyKartVariant(int rawIndex)
    {
        ResolveKartVisualRoot();
        if (kartVisualRoot == null || kartModelPrefabs == null || kartModelPrefabs.Length == 0)
        {
            return;
        }

        int variantIndex = ResolveVariantIndex(rawIndex);
        GameObject sourcePrefab = kartModelPrefabs[variantIndex];
        if (sourcePrefab == null)
        {
            Debug.LogWarning($"[PlayerKartAppearance] Kart variant prefab at index {variantIndex} is null.");
            return;
        }

        ApplyModelParts(kartVisualRoot, sourcePrefab.transform);
        kartVisualRoot.gameObject.name = sourcePrefab.name;
        activeVariantIndex = variantIndex;
    }

    private int ResolveVariantIndex(int rawIndex)
    {
        if (kartModelPrefabs == null || kartModelPrefabs.Length == 0) return 0;
        if (rawIndex < 0) return 0;
        return rawIndex % kartModelPrefabs.Length;
    }

    private void ApplyModelParts(Transform targetRoot, Transform sourceRoot)
    {
        MeshRenderer[] targetRenderers = targetRoot.GetComponentsInChildren<MeshRenderer>(true);
        foreach (MeshRenderer targetRenderer in targetRenderers)
        {
            Transform sourcePart = FindDescendantByName(sourceRoot, targetRenderer.gameObject.name);
            if (sourcePart == null) continue;

            if (sourcePart.TryGetComponent(out MeshFilter sourceFilter) &&
                targetRenderer.TryGetComponent(out MeshFilter targetFilter) &&
                sourceFilter.sharedMesh != null)
            {
                targetFilter.sharedMesh = sourceFilter.sharedMesh;
            }

            if (sourcePart.TryGetComponent(out MeshRenderer sourceRenderer))
            {
                targetRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
            }
        }
    }

    private static Transform FindDescendantByName(Transform parent, string targetName)
    {
        if (parent.name == targetName) return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform match = FindDescendantByName(parent.GetChild(i), targetName);
            if (match != null) return match;
        }

        return null;
    }

    private void ResolveKartVisualRoot()
    {
        if (kartVisualRoot != null) return;

        Transform found = transform.Find("raceCarRed");
        if (found != null)
        {
            kartVisualRoot = found;
            return;
        }

        MeshRenderer firstRenderer = GetComponentInChildren<MeshRenderer>(true);
        if (firstRenderer != null && firstRenderer.transform.parent != null && firstRenderer.transform.parent != transform)
        {
            kartVisualRoot = firstRenderer.transform.parent;
        }
    }

    private void UnsubscribeKartIndex()
    {
        if (!isSubscribed) return;
        KartIndex.OnValueChanged -= HandleKartIndexChanged;
        isSubscribed = false;
    }
}
