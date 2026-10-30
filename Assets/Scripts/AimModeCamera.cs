using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Keeps the camera rig in step with the player's aiming mode.
///
/// The two modes need different cameras. Mouse aim is a third-person aim camera:
/// <see cref="CinemachineThirdPersonAim"/> holds the screen centre on the guns' aim so the reticle is
/// honest. Travel aim has no screen-centred aim at all - the guns go where the ball is going - so
/// that camera would be staring at a point the player is not shooting at. In its place
/// <see cref="OrbitCamera"/> turns the rig into a plain distance orbit around a point above the
/// player, keeping the player low in frame while the mouse circles it.
///
/// Only the aim behaviour is swapped. The third-person body is left running in both modes, so
/// <see cref="OrbitCamera"/> can overwrite the body's output without the rig losing its state, and
/// switching back to mouse aim does not snap the camera.
/// </summary>
[DisallowMultipleComponent]
public class AimModeCamera : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The player whose aiming mode drives the swap. Found in the scene when left empty.")]
    [SerializeField] Player player;
    [Tooltip("The aim behaviour used while the player aims with the mouse.")]
    [SerializeField] CinemachineThirdPersonAim aimCamera;
    [Tooltip("The orbit that replaces the third-person aim while the player aims along the movement direction.")]
    [SerializeField] OrbitCamera orbitCamera;

    // Which mode the rig is currently set up for, so the poll below is a no-op when nothing changed.
    AimMode appliedMode = (AimMode)(-1);

    void Awake()
    {
        Resolve();
    }

    // Polled, not pushed through an event: there is exactly one listener, and a comparison per frame
    // is simpler than a subscription plus an Awake/OnEnable/Start ordering dance. The mode is only
    // ever compared, never copied, so a one-frame lag at worst is the whole cost.
    void Update()
    {
        if (player == null) player = FindAnyObjectByType<Player>();
        Apply(player != null ? player.CurrentAimMode : AimMode.Mouse);
    }

    void Resolve()
    {
        if (player == null) player = FindAnyObjectByType<Player>();
        if (aimCamera == null) aimCamera = GetComponent<CinemachineThirdPersonAim>();
        if (orbitCamera == null) orbitCamera = GetComponent<OrbitCamera>();
    }

    /// <summary>
    /// Toggles the two aim behaviours. Disabling the extension on the way out of orbit matters: a
    /// disabled extension is not called, so it releases the body output completely.
    /// </summary>
    void Apply(AimMode mode)
    {
        if (appliedMode == mode) return;
        appliedMode = mode;

        bool orbit = mode == AimMode.Movement;

        if (aimCamera != null) aimCamera.enabled = !orbit;
        if (orbitCamera != null) orbitCamera.enabled = orbit;

        if (orbit && orbitCamera == null)
        {
            Debug.LogError($"{name}: travel aim needs an OrbitCamera on this camera, and there is " +
                           $"none.", this);
        }
    }
}
