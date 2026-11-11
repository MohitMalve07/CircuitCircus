using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Controls the Settings scene UI: Master Volume, Music Volume, Steering Sensitivity sliders,
/// MiniMap toggle (swapping background sprite between OnnToggle and OffToggle), Reset, and Back.
/// </summary>
public class SettingsManager : MonoBehaviour
{
    [Header("Sliders")]
    public Slider masterVolumeSlider;
    public Slider musicSlider;
    public Slider steeringSensitivitySlider;

    [Header("MiniMap Toggle")]
    public Toggle miniMapToggle;
    public Image miniMapBackground;
    public Sprite onnToggleSprite;
    public Sprite offToggleSprite;

    [Header("Buttons")]
    public Button resetButton;
    public Button backButton;

    private bool listenersWired = false;

    private void Awake()
    {
        AutoFindReferencesIfNeeded();
        PrepareMiniMapToggleVisual();
        SyncUIFromSettings();
        WireListeners();
        GameSettings.ApplySceneAudio();
    }

    private void AutoFindReferencesIfNeeded()
    {
        if (masterVolumeSlider == null) masterVolumeSlider = FindComponentByName<Slider>("MasterVolume");
        if (musicSlider == null) musicSlider = FindComponentByName<Slider>("Music");
        if (steeringSensitivitySlider == null) steeringSensitivitySlider = FindComponentByName<Slider>("SteeringSensitivity");
        if (miniMapToggle == null) miniMapToggle = FindComponentByName<Toggle>("MiniMap Toggle");

        if (miniMapBackground == null && miniMapToggle != null)
        {
            Transform bg = miniMapToggle.transform.Find("Background");
            if (bg != null) miniMapBackground = bg.GetComponent<Image>();
        }

        if (resetButton == null) resetButton = FindComponentByName<Button>("Reset");
        if (backButton == null) backButton = FindComponentByName<Button>("Back");
    }

    private void PrepareMiniMapToggleVisual()
    {
        if (miniMapToggle != null && miniMapToggle.graphic != null)
        {
            // Toggle state is communicated by swapping Background between OnnToggle and OffToggle
            miniMapToggle.graphic.enabled = false;
        }
    }

    private void SyncUIFromSettings()
    {
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
        }

        if (musicSlider != null)
        {
            musicSlider.SetValueWithoutNotify(GameSettings.MusicVolume);
        }

        if (steeringSensitivitySlider != null)
        {
            steeringSensitivitySlider.SetValueWithoutNotify(GameSettings.SteeringSensitivity);
        }

        bool miniMapOn = GameSettings.MiniMapEnabled;
        if (miniMapToggle != null)
        {
            miniMapToggle.SetIsOnWithoutNotify(miniMapOn);
        }

        UpdateMiniMapToggleSprite(miniMapOn);
    }

    private void WireListeners()
    {
        if (listenersWired) return;
        listenersWired = true;

        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);

        if (musicSlider != null)
            musicSlider.onValueChanged.AddListener(OnMusicVolumeChanged);

        if (steeringSensitivitySlider != null)
            steeringSensitivitySlider.onValueChanged.AddListener(OnSteeringSensitivityChanged);

        if (miniMapToggle != null)
            miniMapToggle.onValueChanged.AddListener(OnMiniMapToggled);

        if (resetButton != null && resetButton.onClick.GetPersistentEventCount() == 0)
            resetButton.onClick.AddListener(OnResetClicked);

        if (backButton != null && backButton.onClick.GetPersistentEventCount() == 0)
            backButton.onClick.AddListener(OnBackClicked);
    }

    public void OnMasterVolumeChanged(float value)
    {
        GameSettings.MasterVolume = value;
    }

    public void OnMusicVolumeChanged(float value)
    {
        GameSettings.MusicVolume = value;
    }

    public void OnSteeringSensitivityChanged(float value)
    {
        GameSettings.SteeringSensitivity = value;
    }

    public void OnMiniMapToggled(bool isOn)
    {
        GameSettings.MiniMapEnabled = isOn;
        UpdateMiniMapToggleSprite(isOn);
    }

    public void OnResetClicked()
    {
        GameSettings.ResetToDefaults();
        SyncUIFromSettings();
        GameSettings.ApplySceneAudio();
        GameSettings.ApplyMiniMapSetting();
    }

    public void OnBackClicked()
    {
        PlayerPrefs.Save();

        if (SceneManager.sceneCount > 1)
        {
            SceneManager.UnloadSceneAsync(gameObject.scene);
        }
        else
        {
            SceneManager.LoadScene("Main Menu", LoadSceneMode.Single);
        }
    }

    private void UpdateMiniMapToggleSprite(bool isOn)
    {
        if (miniMapBackground == null) return;

        Sprite targetSprite = isOn ? onnToggleSprite : offToggleSprite;
        if (targetSprite != null)
        {
            miniMapBackground.sprite = targetSprite;
        }
    }

    private static T FindComponentByName<T>(string objectName) where T : Component
    {
        foreach (T component in FindObjectsByType<T>(FindObjectsInactive.Include))
        {
            if (component.gameObject.name == objectName)
            {
                return component;
            }
        }
        return null;
    }

    private void OnDestroy()
    {
        if (!listenersWired) return;
        listenersWired = false;

        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.RemoveListener(OnMasterVolumeChanged);

        if (musicSlider != null)
            musicSlider.onValueChanged.RemoveListener(OnMusicVolumeChanged);

        if (steeringSensitivitySlider != null)
            steeringSensitivitySlider.onValueChanged.RemoveListener(OnSteeringSensitivityChanged);

        if (miniMapToggle != null)
            miniMapToggle.onValueChanged.RemoveListener(OnMiniMapToggled);

        if (resetButton != null)
            resetButton.onClick.RemoveListener(OnResetClicked);

        if (backButton != null)
            backButton.onClick.RemoveListener(OnBackClicked);
    }
}
