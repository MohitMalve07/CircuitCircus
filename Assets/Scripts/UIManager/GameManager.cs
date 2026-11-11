using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

/// <summary>
/// Manages the race flow: countdown, race start, and race finish detection.
/// Reads race state from the local player's RaceProgress NetworkVariables.
/// 
/// SETUP:
/// 1. Place in the Game scene.
/// 2. Either assign UI Text references in Inspector, OR set the name fields
///    to match your UI Text GameObjects (they'll be found automatically).
/// </summary>
public class GameManager : MonoBehaviour
{
    [Header("UI References (optional — auto-found by name if null)")]
    public Text countdownText;
    public Text lapText;

    [Header("UI Object Names (used if references above are null)")]
    [Tooltip("Name of the countdown Text GameObject in the scene.")]
    public string countdownTextName = "CountdownText";

    [Tooltip("Name of the lap counter Text GameObject in the scene.")]
    public string lapTextName = "Lap";

    [Header("Race Settings")]
    public float raceEndDelay = 3f;

    private bool raceFinished = false;

    // Cached reference to the local player's RaceProgress
    private RaceProgress localPlayerProgress;

    private void Start()
    {
        // Apply persisted audio and minimap settings
        GameSettings.ApplySceneAudio();
        GameSettings.ApplyMiniMapSetting(this);

        // Auto-find UI references if not assigned in Inspector
        FindUIReferences();
        StartCoroutine(StartCountdown());
    }

    private void FindUIReferences()
    {
        // Search all Text components including under inactive parents
        foreach (var text in FindObjectsByType<Text>(FindObjectsInactive.Include))
        {
            if (countdownText == null && text.gameObject.name == countdownTextName)
                countdownText = text;
            else if (lapText == null && text.gameObject.name == lapTextName)
                lapText = text;
        }

        if (countdownText == null)
            Debug.LogWarning($"[GameManager] Countdown Text not found (Inspector empty, no object named '{countdownTextName}').");
        if (lapText == null)
            Debug.LogWarning($"[GameManager] Lap Text not found (Inspector empty, no object named '{lapTextName}').");
        else
            Debug.Log($"[GameManager] Found lap text: '{lapText.gameObject.name}'");
    }

    IEnumerator StartCountdown()
    {
        for (int i = 3; i > 0; i--)
        {
            if (countdownText != null) countdownText.text = i.ToString();
            yield return new WaitForSeconds(1f);
        }

        if (countdownText != null) countdownText.text = "GO!";
        yield return new WaitForSeconds(1f);

        if (countdownText != null) countdownText.text = "";
    }

    private void Update()
    {
        // Try to find the local player's RaceProgress if we don't have it yet
        if (localPlayerProgress == null)
        {
            localPlayerProgress = FindLocalPlayerProgress();
            if (localPlayerProgress != null)
            {
                Debug.Log("[GameManager] Found local player's RaceProgress. Subscribing to events.");

                // Subscribe to events
                localPlayerProgress.OnLapChanged += HandleLapChanged;
                localPlayerProgress.OnRaceFinished += HandleRaceFinished;

                // Initialize UI with current state
                UpdateLapUI(localPlayerProgress.Laps);
            }
            return;
        }

        // Update lap UI each frame in case we missed an event
        UpdateLapUI(localPlayerProgress.Laps);
    }

    private RaceProgress FindLocalPlayerProgress()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            return FindAnyObjectByType<RaceProgress>();
        }

        if (NetworkManager.Singleton.LocalClient != null &&
            NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            return NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<RaceProgress>();
        }

        return null;
    }

    private void HandleLapChanged(int newLap)
    {
        Debug.Log($"[GameManager] Lap changed event received: {newLap}");
        UpdateLapUI(newLap);
    }

    private void UpdateLapUI(int currentLap)
    {
        if (lapText == null || localPlayerProgress == null) return;

        int displayLap = Mathf.Clamp(currentLap, 1, localPlayerProgress.TotalLapsRequired);
        lapText.text = $"Lap: {displayLap}/{localPlayerProgress.TotalLapsRequired}";
    }

    private void HandleRaceFinished()
    {
        if (raceFinished) return;
        raceFinished = true;

        // Show a local completion message — RaceManager handles the actual results screen
        if (countdownText != null)
            countdownText.text = "Finished!";

        Debug.Log("[GameManager] Local player finished the race.");
    }

    private void OnDestroy()
    {
        if (localPlayerProgress != null)
        {
            localPlayerProgress.OnLapChanged -= HandleLapChanged;
            localPlayerProgress.OnRaceFinished -= HandleRaceFinished;
        }
    }
}
