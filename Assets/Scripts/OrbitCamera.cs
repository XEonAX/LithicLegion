using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// A plain, distance-based orbit for the travel-aim camera.
///
/// The camera sits on a sphere of <see cref="distance"/> around a centre held
/// <see cref="pivotHeight"/> above the follow target. The orbit direction comes from
/// <see cref="lookRig"/> - the transform the mouse turns - so both aiming modes share one look
/// control and only the camera behaviour changes.
///
/// The camera looks at the player, then droops its aim up by <see cref="screenDrop"/> degrees. The
/// droop is applied in camera space, after the look, so the player sits the same distance below the
/// centre of frame at every yaw and every pitch. Framing off a point held above the player would not
/// do that: as the orbit climbs overhead the player slides back towards the middle of the screen.
///
/// There is deliberately no rotational damping. A damped orbit lets a fast mouse sweep get ahead of
/// the camera, and the player drifts out of frame until the camera catches up. The only optional
/// smoothing is <see cref="positionDamping"/>, and that is safe because the camera re-aims at the
/// player every frame: trailing the ball changes how far the camera hangs back, not where the player
/// appears in frame.
/// </summary>
[AddComponentMenu("Cinemachine/Behaviour/Orbit Camera")]
[DisallowMultipleComponent]
public sealed class OrbitCamera : CinemachineExtension
{
    [Header("Orbit")]
    [Tooltip("Distance from the orbit centre to the camera, in metres.")]
    [SerializeField] float distance = 9.75f;
    [Tooltip("Height of the orbit centre above the follow target, in metres. Raising this lifts the " +
             "camera and its whole orbit, without changing where the player sits in frame.")]
    [SerializeField] float pivotHeight = 1f;
    [Tooltip("The transform the mouse look turns - the look rig. Left empty, the follow target's " +
             "parent is used: the follow target itself carries the aim camera's own authored tilt, " +
             "and the orbit should read the mouse pitch, not that tilt.")]
    [SerializeField] Transform lookRig;

    [Header("Framing")]
    [Tooltip("Degrees the camera aims above the player, which is how far below the centre of the " +
             "screen the player sits. It is applied in camera space, so it holds at every orbit angle.")]
    [SerializeField] float screenDrop = 12f;

    [Header("Pitch limits")]
    [Tooltip("Lowest orbit pitch, in degrees. Positive pitch lifts the camera; the lower limit stops " +
             "the camera dropping under the floor when the player looks up.")]
    [SerializeField] float minPitch = -5f;
    [Tooltip("Highest orbit pitch, in degrees. The camera climbs towards straight overhead here.")]
    [SerializeField] float maxPitch = 80f;

    [Header("Smoothing")]
    [Tooltip("Seconds the camera trails the ball by. 0 is rigid. Safe at any value: the camera " +
             "re-aims at the player every frame, so this changes how far behind the ball it hangs, " +
             "not where the player sits in the frame.")]
    [SerializeField] float positionDamping;

    Vector3 smoothedPosition;
    bool hasPosition;

    // Switched out of orbit by AimModeCamera, so the next switch back starts from the real orbit
    // instead of easing in from wherever the camera was left.
    void OnDisable()
    {
        hasPosition = false;
    }

    /// <summary>
    /// Replaces the body stage's position and orientation with the orbit. Running at Body (rather
    /// than Finalize) keeps it in the same slot the third-person follow occupies, which is what the
    /// mode switch is swapping against.
    /// </summary>
    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam,
                                                      CinemachineCore.Stage stage,
                                                      ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Body) return;

        Transform follow = vcam.Follow;
        if (follow == null) return;

        Vector3 pivot = follow.position + Vector3.up * pivotHeight;

        // The look rig turns the orbit. Yaw and pitch are clamped so a mouse sweep cannot take the
        // camera through the floor or over the top into an ill-defined LookRotation.
        Transform turn = lookRig != null ? lookRig : (follow.parent != null ? follow.parent : follow);
        Vector3 euler = turn.rotation.eulerAngles;
        float yaw = euler.y;
        float pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, euler.x), minPitch, maxPitch);

        Vector3 wanted = pivot + Quaternion.Euler(pitch, yaw, 0f) * (Vector3.back * distance);

        // Optional trailing: the camera position lags the orbit, but the look below still targets
        // the player's real position, so the framing is unchanged while the camera catches up.
        if (positionDamping <= 0f || deltaTime < 0f || !hasPosition)
        {
            smoothedPosition = wanted;
            hasPosition = true;
        }
        else
        {
            smoothedPosition = Vector3.Lerp(smoothedPosition, wanted,
                                            1f - Mathf.Exp(-deltaTime / positionDamping));
        }

        state.RawPosition = smoothedPosition;

        // Look at the player, then tip the aim up in camera space. The player never moves relative
        // to the view axis, so it keeps the same spot at the bottom of the screen.
        Quaternion toPlayer = Quaternion.LookRotation(follow.position - state.RawPosition, state.ReferenceUp);
        state.RawOrientation = toPlayer * Quaternion.Euler(-screenDrop, 0f, 0f);
        state.ReferenceLookAt = follow.position;
    }
}
