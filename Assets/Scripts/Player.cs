using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>How the weapons decide which way to point.</summary>
public enum AimMode
{
    /// <summary>
    /// Third-person aim: the guns follow the world point under the centre of the screen, so they can
    /// be swung around and raised or lowered at the world under the reticle.
    /// </summary>
    Mouse,

    /// <summary>
    /// Travel aim: the guns follow the way the ball is actually travelling - its rigidbody velocity,
    /// flattened onto the ground - so they stay level and shots go where the ball is going, whether
    /// that is from the stick, a bounce off a wall, or a blast that shoved it.
    /// </summary>
    Movement,
}

public class Player : MonoBehaviour
{
    [SerializeField] float torque = 10f;

    [Header("Aim")]
    [Tooltip("Aim mode the game starts in. The Aim Mode action (C, or d-pad up on a gamepad) " +
             "switches between them at runtime.")]
    [SerializeField] AimMode startingAimMode = AimMode.Mouse;
    [Tooltip("Layers the mouse aim ray can stop on. Travel aim traces nothing, so it ignores this.")]
    [SerializeField] LayerMask aimLayerMask;
    [Tooltip("Reticle that reports the mouse aim distance. Player hides it in travel aim, where the " +
             "screen centre is no longer where the guns shoot.")]
    [SerializeField] CrossHair crossHair;
    [Tooltip("The pivot the guns hang from. It is rotated to point at the aim.")]
    [SerializeField] Transform Weapons;
    [Tooltip("Travel aim only: the ball's speed, in metres per second, below which it counts as " +
             "standing still and the guns hold their last heading instead of jittering around the " +
             "direction of a barely-moving ball.")]
    [SerializeField] float minAimSpeed = 0.25f;
    [Tooltip("Travel aim only: how slowly the guns swing onto a new direction of travel, in seconds " +
             "to close most of the gap. Damps the noisy direction a ball has while it is skidding " +
             "over a seam or settling out of a collision. 0 swings instantly.")]
    [SerializeField] float aimDamping = 0.08f;

    Camera cam;
    Rigidbody rb;
    InputAction moveAction;
    InputAction aimModeAction;

    AimMode aimMode;

    // The heading the guns are held at, in degrees around world up. Travel aim damps this rather
    // than the transform: the Weapons pivot hangs under the mouse-rotated rig, so reading and
    // writing its world rotation would fold the camera's rotation back into the aim.
    float aimYaw;

    /// <summary>
    /// The direction on the ground the player is currently asking the ball to roll, as a unit
    /// vector, or zero when there is no input. <see cref="SphereAudio"/> reads it to tell a clean
    /// roll from the ball being driven across the way it is already travelling.
    /// </summary>
    public Vector3 DriveDirection { get; private set; }

    /// <summary>
    /// How hard the player is asking the ball to drive, 0-1. Unlike <see cref="DriveDirection"/>
    /// this keeps the analogue weight of a stick, so the motor can idle between nothing and full.
    /// </summary>
    public float DriveThrottle { get; private set; }

    /// <summary>How the weapons are currently being pointed.</summary>
    public AimMode CurrentAimMode => aimMode;

    // Awake, not Start: the camera rig starts polling for the mode straight away, and Awake is the
    // last point every object is guaranteed to have run before anyone's Update.
    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        aimMode = startingAimMode;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        cam = Camera.main;
        moveAction = InputSystem.actions?.FindAction("Move");
        aimModeAction = InputSystem.actions?.FindAction("AimMode");

        if (aimModeAction == null)
        {
            Debug.LogError($"{name}: no 'AimMode' action in the project-wide input actions, so the " +
                           $"aiming mode cannot be switched at runtime.", this);
        }

        if (Weapons == null)
        {
            Debug.LogError($"{name}: no Weapons pivot assigned, so the guns cannot be aimed.", this);
        }

        // A ball at rest has no direction of travel, so start the held heading on the camera's: that
        // is the way the rig faces, and the way a stick would push the ball first.
        if (cam != null)
        {
            Vector3 heading = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (heading.sqrMagnitude > 1e-4f) aimYaw = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;
        }

        ApplyAimMode();
    }

    // Update is called once per frame
    void Update()
    {
        ReadAimSwitch();
    }

    /// <summary>
    /// The aim is applied here rather than in Update because the guns hang under the camera rig,
    /// which <c>Look</c> rotates in Update. Writing their world rotation in Update lets that rotation
    /// land on top of the aim for the rest of the frame, which reads as the guns twitching with every
    /// mouse move. LateUpdate runs after the whole rig has been turned, so the aim is the last word.
    /// </summary>
    void LateUpdate()
    {
        if (aimMode == AimMode.Mouse) AimWithMouse();
        else AimAlongTravel();
    }

    /// <summary>Switches aiming at runtime. The camera rig follows by polling <see cref="CurrentAimMode"/>.</summary>
    public void SetAimMode(AimMode mode)
    {
        if (aimMode == mode) return;

        aimMode = mode;
        ApplyAimMode();
    }

    /// <summary>Puts the reticle in step with the mode: it is only honest while the guns aim at it.</summary>
    void ApplyAimMode()
    {
        if (crossHair != null) crossHair.SetShown(aimMode == AimMode.Mouse);
    }

    void ReadAimSwitch()
    {
        if (aimModeAction == null || !aimModeAction.WasPressedThisFrame()) return;

        SetAimMode(aimMode == AimMode.Mouse ? AimMode.Movement : AimMode.Mouse);
    }

    /// <summary>
    /// Points the guns at the world point under the centre of the screen. A miss leaves them where
    /// they were last aimed, and tells the reticle the target is as far away as it can be.
    /// </summary>
    void AimWithMouse()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null || Weapons == null) return;

        // The centre of the screen is the camera's forward. Building the ray from the transform
        // rather than ScreenPointToRay keeps it independent of the camera's render matrices, which
        // only refresh when the camera actually renders.
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);

        // Cast the ray into the 3D world.
        if (Physics.Raycast(ray, out RaycastHit hitInfo, Mathf.Infinity, aimLayerMask))
        {
            // Point the guns at the exact spot in 3D space the reticle is sitting on.
            Weapons.LookAt(hitInfo.point);

            ReportAimDistance(hitInfo.distance);
        }
        else
        {
            // Aiming at nothing, so the reticle reads as a target right out at range.
            ReportAimDistance(float.PositiveInfinity);
        }
    }

    /// <summary>
    /// Points the guns along the way the ball is actually travelling. The velocity is flattened onto
    /// the ground on the way in, so this is a yaw-only aim: the guns never tip up or down, whatever
    /// the camera or the floor under the ball is doing.
    /// </summary>
    void AimAlongTravel()
    {
        // The ball's real velocity, not the stick's command: after a bounce, a dash or a blast it is
        // travelling somewhere the input never asked for, and that is the direction the shots go.
        Vector3 travel = rb != null ? Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up) : Vector3.zero;

        // Below the threshold the direction of travel is noise; hold the last heading instead.
        if (travel.sqrMagnitude > minAimSpeed * minAimSpeed)
        {
            // A rigidbody's velocity direction is only as clean as the contact under it: skidding
            // over a seam, settling out of a collision or rolling at a crawl all make it twitch.
            // Ease the held heading onto the new one with the frame-rate-independent exponential
            // form of a one-pole filter. Yaw is a scalar here, so a bounce can turn through 180
            // degrees cleanly and the result never depends on where the transform already points.
            float wantedYaw = Mathf.Atan2(travel.x, travel.z) * Mathf.Rad2Deg;
            float blend = aimDamping > 0f ? 1f - Mathf.Exp(-Time.deltaTime / aimDamping) : 1f;
            aimYaw = Mathf.LerpAngle(aimYaw, wantedYaw, blend);
        }

        if (Weapons == null) return;

        // Written exactly, not slerped: the damping already lives in aimYaw, and starting from the
        // transform's current rotation would fold the rig's mouse rotation back in.
        Vector3 direction = Quaternion.Euler(0f, aimYaw, 0f) * Vector3.forward;
        Weapons.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    /// <summary>Tells the reticle how far away the aim point is, so it can size itself to match.</summary>
    void ReportAimDistance(float distance)
    {
        if (crossHair != null) crossHair.SetDistance(distance);
    }
    void FixedUpdate()
    {
        if (moveAction == null || rb == null) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Vector2 input = moveAction.ReadValue<Vector2>();
        DriveThrottle = Mathf.Clamp01(input.magnitude);

        // Direction of travel relative to the camera, flattened onto the ground plane.
        Vector3 direction = new Vector3(input.x, 0f, input.y);
        direction = cam.transform.TransformDirection(direction);
        direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        DriveDirection = direction;

        // A sphere rolls about an axis perpendicular to where it's going, so the roll
        // axis is "up x direction". 
        Vector3 rollAxis = Vector3.Cross(Vector3.up, direction);
        rb.AddTorque(rollAxis * torque);
    }
}
