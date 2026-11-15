using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Drives smooth, dynamic golden-afternoon to sunset lighting progression during gameplay.
/// Sweeps the sun angle so kart and track shadows evolve organically across the race,
/// while blending skybox, cool-warm trilight ambient, fill light, and horizon fog.
/// </summary>
public class DynamicRaceLighting : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Light sunLight;
    [SerializeField] private Light fillLight;
    [SerializeField] private Material skyboxMaterial;
    [SerializeField] private ReflectionProbe reflectionProbe;

    [Header("Time-of-Day Cycle")]
    [Tooltip("Enable continuous progression of sun angle and atmosphere during the race.")]
    [SerializeField] private bool animateCycle = true;

    [Tooltip("Seconds for a full progression between start-of-race afternoon and golden hour.")]
    [Min(10f)]
    [SerializeField] private float cycleDurationSeconds = 180f;

    [Tooltip("Starting point in the lighting curve (0 = crisp afternoon, 1 = deep golden hour).")]
    [Range(0f, 1f)]
    [SerializeField] private float startNormalizedTime = 0.2f;

    [Header("Sun Motion")]
    [SerializeField] private float startElevation = 36f;
    [SerializeField] private float endElevation = 21f;
    [SerializeField] private float startAzimuth = -48f;
    [SerializeField] private float azimuthSweep = 70f;

    [Header("Sun & Fill Lighting")]
    [SerializeField] private Color afternoonSunColor = new Color(1.00f, 0.94f, 0.85f);
    [SerializeField] private Color goldenSunColor = new Color(1.00f, 0.74f, 0.48f);
    [SerializeField] private float afternoonSunIntensity = 1.08f;
    [SerializeField] private float goldenSunIntensity = 0.96f;

    [SerializeField] private Color afternoonFillColor = new Color(0.45f, 0.70f, 1.00f);
    [SerializeField] private Color goldenFillColor = new Color(0.42f, 0.54f, 0.88f);
    [SerializeField] private float afternoonFillIntensity = 0.22f;
    [SerializeField] private float goldenFillIntensity = 0.18f;

    [Header("Ambient & Fog")]
    [SerializeField] private Color afternoonSkyAmbient = new Color(0.20f, 0.34f, 0.58f);
    [SerializeField] private Color goldenSkyAmbient = new Color(0.22f, 0.28f, 0.50f);
    [SerializeField] private Color equatorAmbient = new Color(0.32f, 0.37f, 0.44f);
    [SerializeField] private Color groundAmbient = new Color(0.14f, 0.19f, 0.12f);

    [SerializeField] private Color afternoonFogColor = new Color(0.68f, 0.81f, 0.95f);
    [SerializeField] private Color goldenFogColor = new Color(0.86f, 0.69f, 0.58f);

    [Header("Skybox")]
    [SerializeField] private float afternoonAtmosphere = 1.05f;
    [SerializeField] private float goldenAtmosphere = 1.55f;
    [SerializeField] private Color afternoonSkyTint = new Color(0.38f, 0.62f, 0.96f);
    [SerializeField] private Color goldenSkyTint = new Color(0.58f, 0.54f, 0.76f);

    private static readonly int AtmosphereProp = Shader.PropertyToID("_AtmosphereThickness");
    private static readonly int SkyTintProp = Shader.PropertyToID("_SkyTint");

    private float elapsedCycleTime;
    private Material runtimeSkyboxInstance;

    private void Awake()
    {
        ResolveReferences();

        if (skyboxMaterial != null)
        {
            // Clone the material at runtime so Play Mode never mutates the shared asset on disk.
            runtimeSkyboxInstance = new Material(skyboxMaterial);
            RenderSettings.skybox = runtimeSkyboxInstance;
        }

        RenderSettings.ambientMode = AmbientMode.Trilight;
        ApplyLightingAtNormalizedTime(startNormalizedTime);

        if (reflectionProbe != null)
        {
            reflectionProbe.RenderProbe();
        }
    }

    private void Update()
    {
        if (!animateCycle || cycleDurationSeconds <= 0f) return;

        elapsedCycleTime += Time.deltaTime;
        float pingPong = Mathf.PingPong(startNormalizedTime + elapsedCycleTime / cycleDurationSeconds, 1f);
        ApplyLightingAtNormalizedTime(pingPong);
    }

    private void OnDestroy()
    {
        if (runtimeSkyboxInstance != null)
        {
            if (RenderSettings.skybox == runtimeSkyboxInstance)
            {
                RenderSettings.skybox = skyboxMaterial;
            }
            Destroy(runtimeSkyboxInstance);
        }
    }

    /// <summary>
    /// Applies sun orientation, shadow-casting colors, fill light, trilight ambient, fog, and skybox for t in [0, 1].
    /// </summary>
    public void ApplyLightingAtNormalizedTime(float t)
    {
        t = Mathf.Clamp01(t);

        float elevation = Mathf.Lerp(startElevation, endElevation, t);
        float azimuth = startAzimuth + azimuthSweep * t;

        if (sunLight != null)
        {
            sunLight.transform.rotation = Quaternion.Euler(elevation, azimuth, 0f);
            sunLight.color = Color.Lerp(afternoonSunColor, goldenSunColor, t);
            sunLight.intensity = Mathf.Lerp(afternoonSunIntensity, goldenSunIntensity, t);
        }

        if (fillLight != null)
        {
            fillLight.transform.rotation = Quaternion.Euler(Mathf.Lerp(26f, 20f, t), azimuth + 180f, 0f);
            fillLight.color = Color.Lerp(afternoonFillColor, goldenFillColor, t);
            fillLight.intensity = Mathf.Lerp(afternoonFillIntensity, goldenFillIntensity, t);
        }

        RenderSettings.ambientSkyColor = Color.Lerp(afternoonSkyAmbient, goldenSkyAmbient, t);
        RenderSettings.ambientEquatorColor = equatorAmbient;
        RenderSettings.ambientGroundColor = groundAmbient;
        RenderSettings.fogColor = Color.Lerp(afternoonFogColor, goldenFogColor, t);

        Material targetSky = runtimeSkyboxInstance != null ? runtimeSkyboxInstance : skyboxMaterial;
        if (targetSky != null)
        {
            if (targetSky.HasProperty(AtmosphereProp))
            {
                targetSky.SetFloat(AtmosphereProp, Mathf.Lerp(afternoonAtmosphere, goldenAtmosphere, t));
            }
            if (targetSky.HasProperty(SkyTintProp))
            {
                targetSky.SetColor(SkyTintProp, Color.Lerp(afternoonSkyTint, goldenSkyTint, t));
            }
        }
    }

    private void ResolveReferences()
    {
        if (sunLight == null)
        {
            sunLight = RenderSettings.sun;
        }
        if (skyboxMaterial == null)
        {
            skyboxMaterial = RenderSettings.skybox;
        }
    }
}
