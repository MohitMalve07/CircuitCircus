using UnityEngine;

/// <summary>
/// Central PlayerPrefs-backed persistence and live application for game settings
/// (Master Volume, Music Volume, Steering Sensitivity, and MiniMap visibility).
/// </summary>
public static class GameSettings
{
    public const string MasterVolumeKey = "Settings_MasterVolume";
    public const string MusicVolumeKey = "Settings_MusicVolume";
    public const string SteeringSensitivityKey = "Settings_SteeringSensitivity";
    public const string MiniMapEnabledKey = "Settings_MiniMapEnabled";

    public const float DefaultMasterVolume = 1f;
    public const float DefaultMusicVolume = 1f;
    public const float DefaultSteeringSensitivity = 0.5f;
    public const bool DefaultMiniMapEnabled = true;

    public const float MinSteeringMultiplier = 0.5f;
    public const float MaxSteeringMultiplier = 1.5f;

    public static float MasterVolume
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumeKey, DefaultMasterVolume));
        set
        {
            float clamped = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(MasterVolumeKey, clamped);
            AudioListener.volume = clamped;
        }
    }

    public static float MusicVolume
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumeKey, DefaultMusicVolume));
        set
        {
            float clamped = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(MusicVolumeKey, clamped);
            ApplyMusicVolume();
        }
    }

    public static float SteeringSensitivity
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(SteeringSensitivityKey, DefaultSteeringSensitivity));
        set => PlayerPrefs.SetFloat(SteeringSensitivityKey, Mathf.Clamp01(value));
    }

    public static float SteeringMultiplier =>
        Mathf.Lerp(MinSteeringMultiplier, MaxSteeringMultiplier, SteeringSensitivity);

    public static bool MiniMapEnabled
    {
        get => PlayerPrefs.GetInt(MiniMapEnabledKey, DefaultMiniMapEnabled ? 1 : 0) == 1;
        set
        {
            PlayerPrefs.SetInt(MiniMapEnabledKey, value ? 1 : 0);
            ApplyMiniMapSetting();
        }
    }

    public static void ResetToDefaults()
    {
        MasterVolume = DefaultMasterVolume;
        MusicVolume = DefaultMusicVolume;
        SteeringSensitivity = DefaultSteeringSensitivity;
        MiniMapEnabled = DefaultMiniMapEnabled;
        PlayerPrefs.Save();
    }

    public static void ApplySceneAudio()
    {
        AudioListener.volume = MasterVolume;
        ApplyMusicVolume();
    }

    public static void ApplyMusicVolume()
    {
        float musicVol = MusicVolume;
        foreach (AudioSource source in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include))
        {
            if (source == null) continue;

            // Background music uses looping AudioSources not driven by kart EngineSound
            if (source.loop && source.GetComponent<EngineSound>() == null)
            {
                source.volume = musicVol;
            }
        }
    }

    public static void ApplyMiniMapSetting(GameManager gameManager = null)
    {
        bool enabled = MiniMapEnabled;

        GameManager targetGameManager = gameManager != null
            ? gameManager
            : Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);

        if (targetGameManager != null)
        {
            Transform miniMapUI = targetGameManager.transform.Find("MiniMap");
            if (miniMapUI != null)
            {
                miniMapUI.gameObject.SetActive(enabled);
            }
        }

        GameObject minimapCamObj = GameObject.FindGameObjectWithTag("MinimapCamera");
        if (minimapCamObj != null)
        {
            Camera minimapCam = minimapCamObj.GetComponent<Camera>();
            if (minimapCam != null)
            {
                minimapCam.enabled = enabled;
            }
        }
    }
}
