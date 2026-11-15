using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls visual states (Upcoming, NextRequired, Activated), glow pulsing,
/// floating directional arrow animation, particle systems, and confirmation audio
/// for an individual checkpoint without duplicating validation logic.
/// </summary>
[RequireComponent(typeof(Checkpoint))]
public class CheckpointVisuals : MonoBehaviour
{
    public enum CheckpointVisualState
    {
        Upcoming,
        NextRequired,
        Activated
    }

    [Header("Hierarchy References")]
    [SerializeField] private Transform gateVisualsRoot;
    [SerializeField] private Transform particleEffectsRoot;
    [SerializeField] private MeshRenderer[] gateRenderers;
    [SerializeField] private SpriteRenderer leftGlow;
    [SerializeField] private SpriteRenderer rightGlow;
    [SerializeField] private SpriteRenderer arrowRenderer;
    [SerializeField] private Text checkpointNumberText;

    [Header("Particle Systems")]
    [SerializeField] private ParticleSystem continuousParticles;
    [SerializeField] private ParticleSystem leftParticles;
    [SerializeField] private ParticleSystem rightParticles;
    [SerializeField] private ParticleSystem activationBurst;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip activationSfx;

    [Header("State Colors")]
    [SerializeField] private Color upcomingGateColor = new Color(0f, 0.898f, 1f, 1f);        // #00E5FF
    [SerializeField] private Color upcomingGlowColor = new Color(0f, 0.686f, 1f, 0.85f);     // #00AFFF
    [SerializeField] private Color upcomingArrowColor = Color.white;                         // #FFFFFF
    [SerializeField] private Color nextRequiredColor = new Color(1f, 0.843f, 0.251f, 1f);    // #FFD740
    [SerializeField] private Color activatedColor = new Color(0.224f, 1f, 0.533f, 1f);       // #39FF88

    [Header("Animation & Timing")]
    [SerializeField] private float upcomingFloatSpeed = 2.2f;
    [SerializeField] private float upcomingFloatAmplitude = 0.28f;
    [SerializeField] private float nextFloatSpeed = 4.2f;
    [SerializeField] private float nextFloatAmplitude = 0.42f;
    [SerializeField] private float nextPulseSpeed = 5f;
    [SerializeField] private float nextPulseScaleAmount = 0.18f;
    [SerializeField] private float activationFlashDuration = 0.65f;

    [Header("Emission Rates")]
    [SerializeField] private float upcomingContinuousRate = 16f;
    [SerializeField] private float nextContinuousRate = 30f;
    [SerializeField] private float upcomingPillarRate = 6f;
    [SerializeField] private float nextPillarRate = 12f;

    private static readonly int ColorPropId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorPropId = Shader.PropertyToID("_EmissionColor");

    private Checkpoint checkpoint;
    private RaceProgress boundProgress;
    private MaterialPropertyBlock gatePropertyBlock;
    private Material runtimeGlowMaterial;

    private Vector3 arrowBaseLocalPos;
    private Vector3 leftGlowBaseScale;
    private Vector3 rightGlowBaseScale;
    private Vector3 arrowBaseScale;
    private bool cachedBaseTransforms;

    private float activationTimer;
    private float nextBindCheckTime;
    private CheckpointVisualState currentState = CheckpointVisualState.Upcoming;

    public CheckpointVisualState CurrentState => currentState;
    public float LastActivatedTime { get; private set; } = -1f;
    public int NextRequiredCheckpointIndex { get; private set; }

    private void Awake()
    {
        checkpoint = GetComponent<Checkpoint>();
        gatePropertyBlock = new MaterialPropertyBlock();
        CacheBaseTransforms();
        SetupRuntimeGlowMaterial();

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
        }
    }

    private void SetupRuntimeGlowMaterial()
    {
        SpriteRenderer source = leftGlow != null ? leftGlow : (rightGlow != null ? rightGlow : arrowRenderer);
        if (source == null || source.sharedMaterial == null) return;

        // Neutralize base material tint to white so SpriteRenderer.color drives Cyan/Yellow/Green accurately.
        runtimeGlowMaterial = new Material(source.sharedMaterial);
        runtimeGlowMaterial.color = Color.white;
        if (leftGlow != null) leftGlow.sharedMaterial = runtimeGlowMaterial;
        if (rightGlow != null) rightGlow.sharedMaterial = runtimeGlowMaterial;
        if (arrowRenderer != null) arrowRenderer.sharedMaterial = runtimeGlowMaterial;
    }

    private void Start()
    {
        CacheBaseTransforms();
        TryBindLocalPlayer();
        RefreshVisualState();
    }

    private void Update()
    {
        if (boundProgress == null && Time.time >= nextBindCheckTime)
        {
            nextBindCheckTime = Time.time + 0.2f;
            if (TryBindLocalPlayer())
            {
                RefreshVisualState();
            }
        }

        if (activationTimer > 0f)
        {
            activationTimer = Mathf.Max(0f, activationTimer - Time.deltaTime);
            if (activationTimer <= 0f)
            {
                RefreshVisualState();
            }
        }

        AnimateVisuals();
        ApplyCurrentColors();
    }

    private void OnDestroy()
    {
        UnbindProgress();
        if (runtimeGlowMaterial != null)
        {
            Destroy(runtimeGlowMaterial);
        }
    }

    /// <summary>
    /// Plays the green flash, activation particle burst, and confirmation SFX for this checkpoint.
    /// </summary>
    public void PlayActivationEffect()
    {
        activationTimer = activationFlashDuration;
        LastActivatedTime = Time.time;
        currentState = CheckpointVisualState.Activated;

        if (activationBurst != null)
        {
            if (!activationBurst.gameObject.activeSelf)
            {
                activationBurst.gameObject.SetActive(true);
            }
            activationBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            activationBurst.Play();
        }

        if (audioSource != null && activationSfx != null)
        {
            audioSource.PlayOneShot(activationSfx);
        }

        ApplyCurrentColors();
    }

    /// <summary>
    /// Refreshes label text after CheckpointRegistry index assignment or editor setup.
    /// </summary>
    public void UpdateNumberLabel(int index)
    {
        if (checkpointNumberText == null) return;
        checkpointNumberText.text = index == 0 ? "START / FINISH" : $"CP {index:00}";
    }

    private void CacheBaseTransforms()
    {
        if (cachedBaseTransforms) return;

        if (arrowRenderer != null)
        {
            arrowBaseLocalPos = arrowRenderer.transform.localPosition;
            arrowBaseScale = arrowRenderer.transform.localScale;
        }
        if (leftGlow != null)
        {
            leftGlowBaseScale = leftGlow.transform.localScale;
        }
        if (rightGlow != null)
        {
            rightGlowBaseScale = rightGlow.transform.localScale;
        }

        cachedBaseTransforms = arrowRenderer != null || leftGlow != null || rightGlow != null;
    }

    private bool TryBindLocalPlayer()
    {
        RaceProgress localProgress = FindLocalRaceProgress();
        if (localProgress == null || localProgress == boundProgress)
        {
            return false;
        }

        UnbindProgress();
        boundProgress = localProgress;
        boundProgress.CurrentCheckpointIndex.OnValueChanged += HandleCheckpointIndexChanged;
        boundProgress.CurrentLap.OnValueChanged += HandleLapChanged;
        boundProgress.RaceFinished.OnValueChanged += HandleRaceFinished;

        UpdateNumberLabel(checkpoint != null ? checkpoint.checkpointIndex : 0);
        return true;
    }

    private static RaceProgress FindLocalRaceProgress()
    {
        bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        RaceProgress[] players = FindObjectsByType<RaceProgress>();
        foreach (RaceProgress progress in players)
        {
            if (progress == null) continue;
            if (!networked)
            {
                return progress;
            }
            if (progress.IsSpawned && progress.IsOwner)
            {
                return progress;
            }
        }
        return null;
    }

    private void UnbindProgress()
    {
        if (boundProgress == null) return;

        boundProgress.CurrentCheckpointIndex.OnValueChanged -= HandleCheckpointIndexChanged;
        boundProgress.CurrentLap.OnValueChanged -= HandleLapChanged;
        boundProgress.RaceFinished.OnValueChanged -= HandleRaceFinished;
        boundProgress = null;
    }

    private void HandleCheckpointIndexChanged(int previousValue, int newValue)
    {
        if (checkpoint != null && checkpoint.checkpointIndex > 0 && newValue == checkpoint.checkpointIndex)
        {
            PlayActivationEffect();
        }
        RefreshVisualState();
    }

    private void HandleLapChanged(int previousValue, int newValue)
    {
        if (checkpoint != null && checkpoint.checkpointIndex == 0 && newValue > previousValue)
        {
            PlayActivationEffect();
        }
        RefreshVisualState();
    }

    private void HandleRaceFinished(bool previousValue, bool newValue)
    {
        if (checkpoint != null && checkpoint.checkpointIndex == 0 && newValue && !previousValue)
        {
            PlayActivationEffect();
        }
        RefreshVisualState();
    }

    private void RefreshVisualState()
    {
        NextRequiredCheckpointIndex = ComputeNextRequiredIndex();

        if (activationTimer > 0f)
        {
            currentState = CheckpointVisualState.Activated;
        }
        else if (checkpoint != null && checkpoint.checkpointIndex == NextRequiredCheckpointIndex)
        {
            currentState = CheckpointVisualState.NextRequired;
        }
        else
        {
            currentState = CheckpointVisualState.Upcoming;
        }

        UpdateParticleParameters();
        ApplyCurrentColors();
    }

    private int ComputeNextRequiredIndex()
    {
        if (boundProgress == null)
        {
            return 0;
        }
        if (boundProgress.IsRaceFinished)
        {
            return -1;
        }

        int currentCp = boundProgress.CurrentCheckpointIndex.Value;
        if (boundProgress.Laps == 0 && currentCp == 0)
        {
            return 0;
        }

        return currentCp < boundProgress.totalTrackCheckpoints ? currentCp + 1 : 0;
    }

    private void AnimateVisuals()
    {
        bool isNext = currentState == CheckpointVisualState.NextRequired;

        if (arrowRenderer != null)
        {
            float speed = isNext ? nextFloatSpeed : upcomingFloatSpeed;
            float amp = isNext ? nextFloatAmplitude : upcomingFloatAmplitude;
            float yOffset = (Mathf.Sin(Time.time * speed) * 0.5f + 0.5f) * amp;
            arrowRenderer.transform.localPosition = arrowBaseLocalPos + Vector3.up * yOffset;

            float arrowPulse = isNext ? 1f + Mathf.Sin(Time.time * nextPulseSpeed) * (nextPulseScaleAmount * 0.5f) : 1f;
            arrowRenderer.transform.localScale = arrowBaseScale * arrowPulse;
        }

        float glowPulse = isNext ? 1f + (Mathf.Sin(Time.time * nextPulseSpeed) * 0.5f + 0.5f) * nextPulseScaleAmount : 1f;
        if (leftGlow != null)
        {
            leftGlow.transform.localScale = leftGlowBaseScale * glowPulse;
        }
        if (rightGlow != null)
        {
            rightGlow.transform.localScale = rightGlowBaseScale * glowPulse;
        }
    }

    private void ApplyCurrentColors()
    {
        Color baseGate = currentState == CheckpointVisualState.NextRequired ? nextRequiredColor : upcomingGateColor;
        Color baseGlow = currentState == CheckpointVisualState.NextRequired
            ? new Color(nextRequiredColor.r, nextRequiredColor.g, nextRequiredColor.b, 0.95f)
            : upcomingGlowColor;
        Color baseArrow = currentState == CheckpointVisualState.NextRequired ? nextRequiredColor : upcomingArrowColor;

        float flashT = activationFlashDuration > 0f ? Mathf.Clamp01(activationTimer / activationFlashDuration) : 0f;
        Color finalGate = Color.Lerp(baseGate, activatedColor, flashT);
        Color finalGlow = Color.Lerp(baseGlow, new Color(activatedColor.r, activatedColor.g, activatedColor.b, 0.98f), flashT);
        Color finalArrow = Color.Lerp(baseArrow, activatedColor, flashT);

        float emissionIntensity = currentState == CheckpointVisualState.NextRequired ? 0.65f : 0.35f;
        emissionIntensity = Mathf.Lerp(emissionIntensity, 0.9f, flashT);

        if (gateRenderers != null && gatePropertyBlock != null)
        {
            foreach (MeshRenderer mr in gateRenderers)
            {
                if (mr == null) continue;
                mr.GetPropertyBlock(gatePropertyBlock);
                gatePropertyBlock.SetColor(ColorPropId, finalGate);
                gatePropertyBlock.SetColor(EmissionColorPropId, finalGate * emissionIntensity);
                mr.SetPropertyBlock(gatePropertyBlock);
            }
        }

        if (leftGlow != null) leftGlow.color = finalGlow;
        if (rightGlow != null) rightGlow.color = finalGlow;
        if (arrowRenderer != null) arrowRenderer.color = finalArrow;
        if (checkpointNumberText != null) checkpointNumberText.color = finalArrow;
    }

    private void UpdateParticleParameters()
    {
        bool isNext = currentState == CheckpointVisualState.NextRequired;
        Color particleColor = isNext ? nextRequiredColor : upcomingGateColor;

        ConfigureAmbientParticles(continuousParticles, particleColor, isNext ? nextContinuousRate : upcomingContinuousRate);
        ConfigureAmbientParticles(leftParticles, particleColor, isNext ? nextPillarRate : upcomingPillarRate);
        ConfigureAmbientParticles(rightParticles, particleColor, isNext ? nextPillarRate : upcomingPillarRate);
    }

    private static void ConfigureAmbientParticles(ParticleSystem ps, Color color, float rateOverTime)
    {
        if (ps == null) return;

        ParticleSystem.MainModule main = ps.main;
        main.startColor = color;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = rateOverTime;
    }
}
