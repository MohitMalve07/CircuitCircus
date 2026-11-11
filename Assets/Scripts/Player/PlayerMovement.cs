using System.Collections;
using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 50f;
    public float maxSpeed = 15f;
    public float drag = 0.98f;
    public float steerAngle = 20f;

    [Header("Steering Visuals")]
    public Transform frontLeftWheel;
    public Transform frontRightWheel;
    public Transform rearLeftWheel;
    public Transform rearRightWheel;
    public float maxSteerAngleVisual = 30f;

    [Header("Ground Check")]
    [Tooltip("Local-space offset from the kart's pivot to the raycast origin (e.g. slightly below center).")]
    public Vector3 groundCheckOffset = new Vector3(0f, 0.1f, 0f);
    public float groundDistance = 0.5f;
    public LayerMask groundMask;
    public float extraGravity = 20f;

    [Header("Stability Settings")]
    [Tooltip("Lowers the physics center of mass. More negative = harder to tip. -0.8 is a good arcade default.")]
    public Vector3 centerOfMassOffset = new Vector3(0f, -0.8f, 0f);
    [Tooltip("Dampens rotational velocity. Higher = less unwanted spinning. 5 is a solid arcade value.")]
    public float angularDragValue = 5f;
    [Tooltip("Maximum allowed pitch/roll angle. Allows natural tilting but prevents flipping.")]
    public float maxTiltAngle = 45f;
    [Tooltip("Strength of the torque smoothly pushing the kart upright.")]
    public float uprightStabilizerStrength = 40f;

    [Header("Input Control")]
    public float inputDelay = 4f; // ⏳ Delay before player can control kart
    private bool canControl = false;

    private Vector3 moveForce;
    private Rigidbody rb;
    private bool isGrounded;

    public float CurrentSpeed => moveForce.magnitude;
    public float MaxSpeed => maxSpeed;


    private void Start()
    {
        rb = GetComponent<Rigidbody>();

        // --- Anti-rollover stability ---
        // Lower center of mass so the kart resists tipping on bumps/collisions
        rb.centerOfMass = centerOfMassOffset;

        // Use dynamic stabilization instead of freezing axes,
        // allowing natural tilt while preventing rollovers.
        rb.constraints = RigidbodyConstraints.None;

        // Angular drag dampens any residual yaw spin from collisions
        rb.angularDamping = angularDragValue;

        Debug.Log("[PlayerMovement] Stability applied — CoM: " + rb.centerOfMass
                  + ", AngDrag: " + rb.angularDamping);

        StartCoroutine(EnableControlAfterDelay());
    }

    private IEnumerator EnableControlAfterDelay()
    {
        yield return new WaitForSeconds(inputDelay);
        canControl = true;
    }

    /// <summary>
    /// Resets the accumulated move force to zero.
    /// Must be called on respawn to prevent residual velocity
    /// from pushing the player away from the checkpoint.
    /// </summary>
    public void ResetMoveForce()
    {
        moveForce = Vector3.zero;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            // Non-owner instances are driven entirely by NetworkTransform replication.
            // Make the Rigidbody kinematic so local physics don't fight the synced transform.
            if (rb == null) rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            Debug.Log($"[PlayerMovement] Non-owner (Client {OwnerClientId}) — Rigidbody set to kinematic.");
        }
    }

    private void FixedUpdate()
    {
        // In a network session, only the owner runs movement and physics.
        // Non-owners are driven by ClientNetworkTransform replication.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (!IsOwner) return;
        }

        GroundCheck();

        if (canControl)
        {
            Movement();

            float inputHorizontal = Input.GetAxis("Horizontal");
            UpdateWheelVisuals(inputHorizontal);
            AnimateWheelSpin();
        }

        if (!isGrounded)
        {
            rb.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);
        }

        // Always apply stabilization to prevent excessive tipping
        ApplyUprightStabilizer();
    }

    void GroundCheck()
    {
        // Raycast from the kart's own position + configurable offset — no child transform needed
        Vector3 origin = transform.position + transform.TransformDirection(groundCheckOffset);
        isGrounded = Physics.Raycast(origin, -transform.up, groundDistance, groundMask);
        Debug.DrawRay(origin, -transform.up * groundDistance, isGrounded ? Color.green : Color.red);
    }

    void Movement()
    {
        float inputVertical = Input.GetAxis("Vertical");
        moveForce += transform.forward * inputVertical * moveSpeed * Time.deltaTime;

        // Clamp speed
        moveForce = Vector3.ClampMagnitude(moveForce, maxSpeed);

        // Apply movement
        transform.position += moveForce * Time.deltaTime;

        // Steering
        float inputHorizontal = Input.GetAxis("Horizontal");
        float speedFactor = Mathf.Clamp01(moveForce.magnitude / maxSpeed);
        float effectiveSteerAngle = steerAngle * GameSettings.SteeringMultiplier;
        transform.Rotate(Vector3.up * inputHorizontal * effectiveSteerAngle * speedFactor * Time.deltaTime);

        // Apply drag
        if (Mathf.Approximately(inputVertical, 0f))
            moveForce *= 0.95f;
        else
            moveForce *= drag;
    }

    void ApplyUprightStabilizer()
    {
        // 1. Smooth torque-based stabilization towards Vector3.up
        Vector3 torque = Vector3.Cross(transform.up, Vector3.up);
        rb.AddTorque(torque * uprightStabilizerStrength, ForceMode.Acceleration);

        // 2. Hard clamp to prevent excessive tipping (maxTiltAngle)
        Vector3 euler = transform.eulerAngles;
        
        float pitch = euler.x;
        float roll = euler.z;

        // Convert 0-360 to -180 to 180
        if (pitch > 180) pitch -= 360;
        if (roll > 180) roll -= 360;

        bool needsClamp = false;

        if (Mathf.Abs(pitch) > maxTiltAngle)
        {
            pitch = Mathf.Clamp(pitch, -maxTiltAngle, maxTiltAngle);
            needsClamp = true;
        }
        
        if (Mathf.Abs(roll) > maxTiltAngle) // Stabilize roll as well
        {
            roll = Mathf.Clamp(roll, -maxTiltAngle, maxTiltAngle);
            needsClamp = true;
        }

        if (needsClamp)
        {
            // Apply clamped rotation safely to Rigidbody
            Quaternion clampedRot = Quaternion.Euler(pitch, euler.y, roll);
            rb.MoveRotation(clampedRot);

            // Dampen angular velocity on X/Z to prevent jittering against the clamp limit
            Vector3 localAngularVel = transform.InverseTransformDirection(rb.angularVelocity);
            localAngularVel.x *= 0.5f;
            localAngularVel.z *= 0.5f;
            rb.angularVelocity = transform.TransformDirection(localAngularVel);
        }
    }

    void UpdateWheelVisuals(float steerInput)
    {
        float steerAngle = steerInput * maxSteerAngleVisual;
        Quaternion targetRotation = Quaternion.Euler(0, steerAngle, 0);

        frontLeftWheel.localRotation = targetRotation;
        frontRightWheel.localRotation = targetRotation;
    }

    void AnimateWheelSpin()
    {
        float spinSpeed = moveForce.magnitude * 360f * Time.deltaTime;
        frontLeftWheel.Rotate(Vector3.right * spinSpeed);
        frontRightWheel.Rotate(Vector3.right * spinSpeed);
        rearLeftWheel.Rotate(Vector3.right * spinSpeed);
        rearRightWheel.Rotate(Vector3.right * spinSpeed);
    }
}
