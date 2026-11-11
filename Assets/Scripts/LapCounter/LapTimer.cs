using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

/// <summary>
/// Tracks lap timing for the local player. Driven by RaceProgress events.
/// Attach to the player prefab alongside RaceProgress.
/// 
/// SETUP:
/// 1. Move this script from the scene to the player prefab.
/// 2. The UI Text references (lapTimeText, bestLapText) will be found dynamically
///    by name at runtime since they're scene objects, not prefab children.
/// </summary>
[RequireComponent(typeof(RaceProgress))]
public class LapTimer : NetworkBehaviour
{
    [Header("UI Object Names (found at runtime)")]
    [Tooltip("Name of the UI Text GameObject showing current lap time.")]
    public string lapTimeTextName = "LapTimeText";

    [Tooltip("Name of the UI Text GameObject showing best lap time.")]
    public string bestLapTextName = "BestLapText";

    private Text lapTimeText;
    private Text bestLapText;

    private float lapTime;
    private bool lapRunning = false;
    private bool isNetworked = false;
    private bool setupComplete = false;

    public float bestLapTime = Mathf.Infinity;

    private RaceProgress raceProgress;

    public override void OnNetworkSpawn()
    {
        isNetworked = true;

        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        // Try setup immediately — may fail if scene isn't loaded yet
        TrySetup();
    }

    private void Start()
    {
        // Offline mode
        if (!isNetworked)
        {
            TrySetup();
        }
    }

    /// <summary>
    /// Attempts to find UI references and subscribe to RaceProgress events.
    /// Returns true if everything is ready, false if it should retry later.
    /// </summary>
    private bool TrySetup()
    {
        if (setupComplete) return true;

        // Get RaceProgress (should always be available on the same prefab)
        if (raceProgress == null)
            raceProgress = GetComponent<RaceProgress>();

        // Try to find UI elements in the scene
        FindUIReferences();

        // Consider setup complete if at least one UI reference is found
        // (the timer can function without best lap text)
        if (lapTimeText == null)
        {
            // Scene UI not loaded yet — retry later
            return false;
        }

        // Subscribe to lap changes from RaceProgress
        if (raceProgress != null)
        {
            raceProgress.OnLapChanged += HandleLapChanged;
            raceProgress.OnRaceFinished += HandleRaceFinished;
        }

        setupComplete = true;
        Debug.Log("[LapTimer] Setup complete — UI references found and events subscribed.");
        return true;
    }

    private void FindUIReferences()
    {
        if (lapTimeText != null && bestLapText != null) return; // Already found

        foreach (var text in FindObjectsByType<Text>(FindObjectsInactive.Include))
        {
            if (lapTimeText == null && text.gameObject.name == lapTimeTextName)
            {
                lapTimeText = text;
                Debug.Log($"[LapTimer] Found lap time text: '{text.gameObject.name}'");
            }
            else if (bestLapText == null && text.gameObject.name == bestLapTextName)
            {
                bestLapText = text;
                Debug.Log($"[LapTimer] Found best lap text: '{text.gameObject.name}'");
            }
        }
    }

    private void HandleLapChanged(int newLap)
    {
        if (newLap <= 1)
        {
            StartLap();
        }
        else
        {
            EndLap();
            StartLap();
        }
    }

    private void HandleRaceFinished()
    {
        EndLap();
        lapRunning = false;
        Debug.Log($"[LapTimer] Race finished! Best lap: {bestLapTime:F2}s");
    }

    void Update()
    {
        if (isNetworked && !IsOwner) return;

        // Keep retrying setup until successful
        if (!setupComplete)
        {
            TrySetup();
            return;
        }

        if (lapRunning)
        {
            lapTime += Time.deltaTime;
            if (lapTimeText != null)
                lapTimeText.text = "Time: " + lapTime.ToString("F2");
        }
    }

    public void StartLap()
    {
        lapTime = 0f;
        lapRunning = true;
    }

    public void EndLap()
    {
        lapRunning = false;

        if (lapTime < bestLapTime)
        {
            bestLapTime = lapTime;
            if (bestLapText != null)
                bestLapText.text = "Best Lap: " + bestLapTime.ToString("F2");
        }

        Debug.Log($"[LapTimer] Lap finished in: {lapTime:F2}s");
    }

    public override void OnNetworkDespawn()
    {
        if (raceProgress != null)
        {
            raceProgress.OnLapChanged -= HandleLapChanged;
            raceProgress.OnRaceFinished -= HandleRaceFinished;
        }
    }

    public override void OnDestroy()
    {
        // Cleanup for offline mode (and safe fallback if destroyed before despawning)
        if (raceProgress != null)
        {
            raceProgress.OnLapChanged -= HandleLapChanged;
            raceProgress.OnRaceFinished -= HandleRaceFinished;
        }

        base.OnDestroy();
    }
}
