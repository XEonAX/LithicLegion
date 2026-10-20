using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Cinemachine;

/// <summary>
/// The camera side of a big shot, split by when it happens:
///
/// - Charge: shake only. The rail gun ramps <see cref="CameraShakeExtension.AmplitudeGain"/> from a
///   small rumble to a heavy one. Nothing on screen flashes yet.
/// - Shot: the flash, the field-of-view punch, the chromatic-aberration spike and the bloom punch all
///   land together.
///
/// Field of view is written straight onto the Camera because this scene's Cinemachine brain has no
/// output camera, so its lens never reaches the renderer.
/// </summary>
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class CameraFx : MonoBehaviour
{
    public static CameraFx Instance { get; private set; }

    [Header("References")]
    [Tooltip("Camera to widen. Defaults to Camera.main.")]
    [SerializeField] Camera targetCamera;
    [Tooltip("Shake profile on the camera.")]
    [SerializeField] CameraShakeExtension shake;
    [Tooltip("Point the charge shake pivots around. Defaults to the Cinemachine camera's Follow " +
             "target, RotationTarget.")]
    [SerializeField] Transform shakePivot;
    [Tooltip("Volume whose chromatic aberration and bloom are driven.")]
    [SerializeField] Volume volume;

    [Header("Charge shake")]
    [Tooltip("Shake gain the moment the charge starts, so the wind-up is felt immediately.")]
    [SerializeField] float chargeShakeFloor = 0.15f;
    [Tooltip("Shake gain through most of the charge. The reference's crossing jitters a little all " +
             "the way through, rather than staying still and then erupting.")]
    [SerializeField] float chargeShake = 0.7f;
    [Tooltip("How far into the charge the shake starts to build, 0-1. Before this it is only the " +
             "floor, which keeps the first few seconds of a long charge very quiet.")]
    [Range(0f, 0.9f)]
    [SerializeField] float chargeShakeStart = 0.25f;
    [Tooltip("How sharply the charge shake ramps once it starts. 1 is linear; above 1 keeps the early " +
             "charge quieter.")]
    [SerializeField] float chargeShakeCurve = 2f;
    [Tooltip("How much faster the noise runs at full charge.")]
    [SerializeField] float chargeFrequency = 1.5f;
    [Tooltip("Extra shake over the last stretch of the charge, on top of the normal ramp. The " +
             "reference's crossing gets noticeably more agitated just before the shot.")]
    [SerializeField] float chargeSurge = 0.5f;
    [Tooltip("Where the late surge begins, as a fraction of the charge.")]
    [Range(0f, 1f)]
    [SerializeField] float chargeSurgeStart = 0.8f;

    [Header("Shot")]
    [Tooltip("Extra shake gain at the shot, on top of the charge.")]
    [SerializeField] float fireShake = 3.2f;
    [Tooltip("Noise speed multiplier at the shot.")]
    [SerializeField] float fireFrequency = 1.5f;
    [Tooltip("Degrees the view widens by at the shot.")]
    [SerializeField] float fireFov = 12f;
    [Tooltip("Aberration added at the shot.")]
    [SerializeField] float fireAberration = 0.35f;
    [Tooltip("Lens distortion at the shot. Negative bows the frame outward, like the reference.")]
    [SerializeField] float fireDistortion = -0.45f;
    [Tooltip("Degrees the horizon tips by at the shot.")]
    [SerializeField] float fireRoll = 5f;
    [Tooltip("Camera-space translation of the shot's jerk, in metres.")]
    [SerializeField] Vector3 fireJerkPosition = new Vector3(0f, 0.1f, -0.25f);
    [Tooltip("Direct rotation of the shot's jerk, in degrees.")]
    [SerializeField] Vector3 fireJerkRotation = new Vector3(2.5f, 0f, 0f);
    [Tooltip("How long the jerk lasts, in seconds.")]
    [SerializeField] float jerkDuration = 0.18f;
    [Tooltip("Extra bloom intensity at the shot.")]
    [SerializeField] float bloomPunch = 3f;
    [Tooltip("Bloom scatter at the shot. Wider scatter gives the flare a bigger halo.")]
    [Range(0f, 1f)]
    [SerializeField] float bloomScatter = 0.75f;
    [Tooltip("How long the shot's reaction takes to die down, in seconds.")]
    [SerializeField] float punchDuration = 0.5f;

    ChromaticAberration aberration;
    LensDistortion distortion;
    Bloom bloom;

    float baseFov;
    float baseAberration;
    float baseDistortion;
    float baseBloom;
    float baseScatter;

    float charge;
    float punchTimer;
    float punchStrength;
    float rollSign = 1f;
    float jerkSign = 1f;

    // The shake is applied to the camera only while it renders, then undone. The camera's transform
    // belongs to Cinemachine and the aim; feeding the shake back into it would move the follow and
    // the crosshair, and in this rig the pipeline would not even see the change.
    Vector3 chargeRotation;
    Vector3 shotRotation;
    Vector3 shotPosition;
    Vector3 basePosition;
    Quaternion baseRotation;
    bool shakeApplied;

    /// <summary>Pose the last render used, after the shake. Diagnostic only.</summary>
    [System.NonSerialized] public Vector3 DebugAppliedPosition;
    /// <summary>Rotation the last render used, after the shake. Diagnostic only.</summary>
    [System.NonSerialized] public Quaternion DebugAppliedRotation;

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
        RenderPipelineManager.endCameraRendering += EndCameraRendering;
    }

    void Awake()
    {
        Instance = this;

        if (targetCamera == null) targetCamera = Camera.main;
        if (shake == null) shake = GetComponent<CameraShakeExtension>();
        if (volume == null) volume = FindAnyObjectByType<Volume>();

        if (shakePivot == null && targetCamera != null)
        {
            CinemachineCamera vcam = targetCamera.GetComponent<CinemachineCamera>();
            if (vcam != null) shakePivot = vcam.Follow;
        }

        if (shakePivot == null)
        {
            GameObject target = GameObject.Find("RotationTarget");
            if (target != null) shakePivot = target.transform;
        }

        if (targetCamera != null) baseFov = targetCamera.fieldOfView;

        if (volume != null)
        {
            VolumeProfile profile = volume.profile;
            if (profile != null)
            {
                if (!profile.TryGet(out aberration))
                {
                    aberration = profile.Add<ChromaticAberration>(true);
                    aberration.intensity.Override(0f);
                }

                if (!profile.TryGet(out distortion))
                {
                    distortion = profile.Add<LensDistortion>(true);
                    distortion.intensity.Override(0f);
                }

                if (!profile.TryGet(out bloom)) bloom = profile.Add<Bloom>(true);

                if (aberration != null)
                {
                    aberration.intensity.overrideState = true;
                    baseAberration = aberration.intensity.value;
                }

                if (distortion != null)
                {
                    distortion.intensity.overrideState = true;
                    baseDistortion = distortion.intensity.value;
                }

                if (bloom != null)
                {
                    bloom.intensity.overrideState = true;
                    bloom.scatter.overrideState = true;
                    baseBloom = bloom.intensity.value;
                    baseScatter = bloom.scatter.value;
                }
            }
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= EndCameraRendering;

        if (shakeApplied && targetCamera != null)
        {
            targetCamera.transform.SetPositionAndRotation(basePosition, baseRotation);
        }
        shakeApplied = false;

        if (targetCamera != null) targetCamera.fieldOfView = baseFov;
        if (aberration != null) aberration.intensity.value = baseAberration;
        if (distortion != null) distortion.intensity.value = baseDistortion;
        if (bloom != null)
        {
            bloom.intensity.value = baseBloom;
            bloom.scatter.value = baseScatter;
        }
        if (shake != null)
        {
            shake.AmplitudeGain = 0f;
            shake.FrequencyGain = 1f;
            shake.ChargeProgress = 0f;
            shake.RollKick = 0f;
            shake.JerkPosition = Vector3.zero;
            shake.JerkRotation = Vector3.zero;
        }

        charge = 0f;
        punchTimer = 0f;
        punchStrength = 0f;
    }

    /// <summary>
    /// Adds the shake to the camera for the duration of its render, then puts it back. This is the
    /// only place the shake touches a transform: Cinemachine keeps the base pose, the aim stays put,
    /// and the offsets cannot feed back into the next frame.
    /// </summary>
    void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera != targetCamera || targetCamera == null) return;

        basePosition = targetCamera.transform.position;
        baseRotation = targetCamera.transform.rotation;

        // Charge: orbit the camera around the target by the charge rotation, so the target stays
        // pinned and the world moves around it. That is what the reference does.
        Quaternion charge = Quaternion.Euler(chargeRotation);
        Vector3 pivot = shakePivot != null ? shakePivot.position : basePosition;
        Vector3 pivotedPosition = pivot + charge * (basePosition - pivot);
        Quaternion pivotedRotation = charge * baseRotation;

        // Shot: a direct camera-space kick on top of the pivoted pose.
        targetCamera.transform.SetPositionAndRotation(
            pivotedPosition + pivotedRotation * shotPosition,
            pivotedRotation * Quaternion.Euler(shotRotation));
        DebugAppliedPosition = targetCamera.transform.position;
        DebugAppliedRotation = targetCamera.transform.rotation;
        shakeApplied = true;
    }

    void EndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (!shakeApplied || camera != targetCamera) return;

        targetCamera.transform.SetPositionAndRotation(basePosition, baseRotation);
        shakeApplied = false;
    }

    /// <summary>How far the rail gun's charge has got, 0 to 1. Shake only - the flash waits for the shot.</summary>
    public void SetCharge(float value)
    {
        charge = Mathf.Clamp01(value);
    }

    /// <summary>Fires the shot's camera reaction. <paramref name="strength"/> 1 is a full rail shot.</summary>
    public void Punch(float strength)
    {
        punchStrength = Mathf.Max(punchStrength, Mathf.Max(0f, strength));
        punchTimer = punchDuration;
        rollSign = Random.value < 0.5f ? -1f : 1f;
        jerkSign = Random.value < 0.5f ? -1f : 1f;
    }

    void LateUpdate()
    {
        if (punchTimer > 0f) punchTimer = Mathf.Max(0f, punchTimer - Time.deltaTime);

        float punch = punchStrength * PunchCurve();

        // The jerk is instantaneous and dies faster than the rest of the shot's reaction.
        float elapsed = punchDuration - punchTimer;
        float jerk = punchTimer > 0f && elapsed < jerkDuration
            ? Mathf.Pow(1f - elapsed / jerkDuration, 3f)
            : 0f;

        if (shake != null)
        {
            shake.ChargeProgress = charge;

            // The crossing barely moves for most of the charge, then gets more agitated in the last
            // stretch. The first few seconds sit at the floor; the build starts after that and is
            // concentrated late.
            float rampT = Mathf.InverseLerp(chargeShakeStart, 1f, charge);
            float chargeRamp = Mathf.Pow(rampT, Mathf.Max(0.01f, chargeShakeCurve));
            float surge = 1f + chargeSurge * Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(chargeSurgeStart, 1f, charge));
            float chargeShakeNow = charge > 0.001f
                ? Mathf.Lerp(chargeShakeFloor, chargeShake, chargeRamp) * surge
                : 0f;

            shake.AmplitudeGain = chargeShakeNow + punch * fireShake;
            shake.FrequencyGain = 1f + charge * (chargeFrequency - 1f) + punch * (fireFrequency - 1f);
            shake.RollKick = punch * fireRoll * rollSign;
            shake.JerkPosition = Vector3.Scale(fireJerkPosition, new Vector3(jerkSign, 1f, 1f)) * jerk;
            shake.JerkRotation = Vector3.Scale(fireJerkRotation, new Vector3(1f, jerkSign, jerkSign)) * jerk;
            shake.Evaluate(Time.deltaTime, out chargeRotation, out shotRotation, out shotPosition);
        }
        else
        {
            chargeRotation = Vector3.zero;
            shotRotation = Vector3.zero;
            shotPosition = Vector3.zero;
        }

        if (targetCamera != null) targetCamera.fieldOfView = baseFov + punch * fireFov;

        if (aberration != null)
        {
            aberration.intensity.value = Mathf.Clamp01(baseAberration + punch * fireAberration);
        }

        if (distortion != null)
        {
            distortion.intensity.value = Mathf.Clamp(baseDistortion + punch * fireDistortion, -1f, 1f);
        }

        if (bloom != null)
        {
            bloom.intensity.value = baseBloom + punch * bloomPunch;
            bloom.scatter.value = Mathf.Lerp(baseScatter, bloomScatter, punch);
        }

        if (punchTimer <= 0f) punchStrength = 0f;
    }

    float PunchCurve()
    {
        if (punchTimer <= 0f || punchDuration <= 0f) return 0f;

        // Fast rise into a short hold, then an eased fall: the field of view is seen ramping up and
        // back down rather than snapping wide on the shot frame.
        float u = 1f - punchTimer / punchDuration;
        const float rise = 0.14f;
        if (u < rise) return Mathf.SmoothStep(0f, 1f, u / rise);
        return Mathf.Pow(1f - (u - rise) / (1f - rise), 2f);
    }
}
