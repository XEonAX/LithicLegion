using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// The rail gun's camera shake, as a profile that <see cref="CameraFx"/> samples once a frame and
/// applies at render time.
///
/// The charge shake is a rotation about the camera's <c>RotationTarget</c>, not about the camera
/// itself. In the reference the player stays pinned while the grid jitters around them; a
/// camera-centred shake would move the player with the world, which is what looked wrong.
/// The shot's jerk and roll are separate: they are a direct camera-space kick, so the flash can
/// throw the whole view for a moment.
/// </summary>
[DisallowMultipleComponent]
public sealed class CameraShakeExtension : CinemachineExtension
{
    [Header("Amount at gain 1")]
    [Tooltip("Noise rotation, in degrees. Scaled by AmplitudeGain.")]
    [SerializeField] Vector3 rotationAmplitude = new Vector3(0.9f, 1.2f, 1.6f);
    [Tooltip("Noise speed per axis.")]
    [SerializeField] Vector3 frequency = new Vector3(3.4f, 2.7f, 4.2f);

    [Header("Charge strain")]
    [Tooltip("How much the charge shake surges and fades instead of holding one level.")]
    [Range(0f, 1f)]
    [SerializeField] float strainDepth = 0.35f;
    [Tooltip("How often the charge shake surges, per second.")]
    [SerializeField] float strainFrequency = 0.9f;
    [Tooltip("Extra jolts near full charge, in degrees.")]
    [SerializeField] float joltRotation = 0.4f;
    [Tooltip("How often the jolts strike at full charge, per second.")]
    [SerializeField] float joltFrequency = 2.4f;

    /// <summary>Overall strength. The rail gun ramps this through the charge and spikes it at the shot.</summary>
    public float AmplitudeGain;

    /// <summary>Noise speed multiplier. Above 1 makes the same shake read as more violent.</summary>
    public float FrequencyGain = 1f;

    /// <summary>A direct roll, in degrees. The rail shot uses this to tip the horizon.</summary>
    public float RollKick;

    /// <summary>A direct camera-space translation for the shot's jerk, in metres.</summary>
    public Vector3 JerkPosition;

    /// <summary>A direct rotation for the shot's jerk, in degrees.</summary>
    public Vector3 JerkRotation;

    /// <summary>How far the rail gun's charge has got, 0 to 1. Drives the strain and the late jolts.</summary>
    [System.NonSerialized] public float ChargeProgress;

    /// <summary>Charge rotation produced by the last <see cref="Evaluate"/> call, in degrees. Diagnostic only.</summary>
    [System.NonSerialized] public Vector3 DebugRotation;

    /// <summary>Shot translation produced by the last <see cref="Evaluate"/> call, in metres. Diagnostic only.</summary>
    [System.NonSerialized] public Vector3 DebugPosition;

    float time;
    float strainTime;
    float joltTime;
    Vector3 seed;

    protected override void Awake()
    {
        base.Awake();
        seed = new Vector3(Random.Range(0f, 100f), Random.Range(100f, 200f), Random.Range(200f, 300f));
    }

    /// <summary>
    /// Samples the shake for one frame.
    /// <paramref name="chargeRotation"/> is the charge's rotation in degrees, meant to be applied
    /// about <c>RotationTarget</c>. <paramref name="shotRotation"/> and <paramref name="shotPosition"/>
    /// are the shot's direct camera-space kick.
    /// </summary>
    public void Evaluate(float deltaTime, out Vector3 chargeRotation, out Vector3 shotRotation,
                         out Vector3 shotPosition)
    {
        chargeRotation = Vector3.zero;
        shotRotation = JerkRotation + new Vector3(0f, 0f, RollKick);
        shotPosition = JerkPosition;

        if (deltaTime < 0f)
        {
            time = 0f;
            strainTime = 0f;
            joltTime = 0f;
            DebugRotation = chargeRotation;
            DebugPosition = shotPosition;
            return;
        }

        strainTime += deltaTime;
        joltTime += deltaTime;

        if (AmplitudeGain <= 0.0001f)
        {
            DebugRotation = chargeRotation;
            DebugPosition = shotPosition;
            return;
        }

        time += deltaTime * FrequencyGain;

        Vector3 noiseRotation = new Vector3(
            Noise(time * frequency.y * 0.8f, seed.x + 51f),
            Noise(time * frequency.z * 0.9f, seed.y + 71f),
            Noise(time * frequency.x * 1.1f, seed.z + 91f));

        // The shake does not hold a level. It swells and eases like the gun pulling against the
        // player's grip, which is what makes the charge read as effort rather than a hum.
        float strain = 1f - strainDepth * (0.5f + 0.5f * Noise(strainTime * strainFrequency, seed.x + 211f));

        // Brief shoves that only appear once the charge is nearly full.
        float jolt = 0f;
        if (ChargeProgress > 0.001f)
        {
            float strike = Noise(joltTime * joltFrequency, seed.y + 311f);
            jolt = Mathf.Pow(Mathf.Max(0f, (strike - 0.45f) / 0.55f), 2f) * Mathf.Pow(ChargeProgress, 3f);
        }

        Vector3 joltDirection = new Vector3(
            Noise(joltTime * 1.9f, seed.z + 401f),
            Noise(joltTime * 1.3f, seed.x + 431f),
            Noise(joltTime * 2.3f, seed.y + 461f));

        chargeRotation = Vector3.Scale(noiseRotation, rotationAmplitude) * (AmplitudeGain * strain)
                         + joltDirection * (joltRotation * jolt);

        DebugRotation = chargeRotation;
        DebugPosition = shotPosition;
    }

    /// <summary>
    /// Cinemachine still calls this, but the shake is applied at render time by
    /// <see cref="CameraFx"/>, so there is nothing to change in the pipeline state.
    /// </summary>
    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage,
                                                      ref CameraState state, float deltaTime)
    {
        if (stage == CinemachineCore.Stage.Finalize && deltaTime < 0f)
        {
            time = 0f;
            strainTime = 0f;
            joltTime = 0f;
        }
    }

    static float Noise(float t, float offset)
    {
        return Mathf.PerlinNoise(t, offset) * 2f - 1f;
    }
}
