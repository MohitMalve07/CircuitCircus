using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(AudioSource))]
public class EngineSound : NetworkBehaviour
{
    public PlayerMovement player; // reference your movement script
    public float maxSpeed = 15f;

    public float minPitch = 0.8f;
    public float maxPitch = 2.0f;

    public float minVolume = 0.3f;
    public float maxVolume = 1.0f;

    public float smoothSpeed = 5f;

    private AudioSource engineAudio;
    private float currentSpeed;

    // Cached flag: true when running a network session
    private bool isNetworked = false;

    void Start()
    {
        engineAudio = GetComponent<AudioSource>();
    }

    public override void OnNetworkSpawn()
    {
        isNetworked = true;

        // Only the local player should hear their own engine sound at full fidelity.
        // Disable this component on non-owner instances to prevent remote karts
        // from running audio logic locally.
        if (!IsOwner)
        {
            engineAudio = GetComponent<AudioSource>();
            if (engineAudio != null) engineAudio.enabled = false;
            enabled = false;
        }
    }

    void Update()
    {
        if (isNetworked && !IsOwner) return;

        // Get actual movement speed
        float targetSpeed = player.CurrentSpeed;

        // Smooth speed
        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, Time.deltaTime * smoothSpeed);

        // Normalize
        float t = Mathf.Clamp01(currentSpeed / maxSpeed);

        // Apply pitch & volume
        engineAudio.pitch = Mathf.Lerp(minPitch, maxPitch, t);
        engineAudio.volume = Mathf.Lerp(minVolume, maxVolume, t);
    }
}