using UnityEngine;

/// <summary>
/// Something a <see cref="Weapon"/> can mount and drive: one piece that knows how to shoot.
///
/// <see cref="Gun"/> is the automatic bullet version. <see cref="PortalGun"/> and <see cref="RailGun"/>
/// are the held versions, so the Weapon that orchestrates them only has to feed the trigger in and
/// never has to know which kind of emitter it is holding.
/// </summary>
public abstract class WeaponEmitter : MonoBehaviour
{
    /// <summary>Which mount this emitter sits on. A Weapon pairs a Left with a Right.</summary>
    public enum Side { Left, Right }

    [Tooltip("Which mount this is. A Weapon pairs a Left with a Right and alternates between them.")]
    [SerializeField] Side side = Side.Right;

    public Side EmitterSide => side;

    /// <summary>
    /// Fire whatever this emitter fires. <paramref name="interval"/> is the weapon's shot interval, so
    /// an emitter can time its own feedback - recoil, charge-up, beam - to the cadence driving it.
    /// </summary>
    public abstract void Fire(float interval);

    /// <summary>
    /// The trigger went down on this emitter's mount. Automatic emitters are fired through
    /// <see cref="Fire"/> instead; the held emitters start aiming, charging or beaming here.
    /// </summary>
    public virtual void OnTriggerDown() { }

    /// <summary>
    /// The trigger is still down this frame. <paramref name="deltaTime"/> is the frame time, so a beam
    /// can re-aim and a charge can fill without the emitter having to read the clock itself.
    /// </summary>
    public virtual void OnTriggerHeld(float deltaTime) { }

    /// <summary>The trigger came up. A held emitter commits whatever it was aiming or charging.</summary>
    public virtual void OnTriggerUp() { }

    /// <summary>
    /// The weapon is putting this emitter away - a mode change or the weapon being disabled. Drop the
    /// beam or the charge without firing, because the player did not release the trigger.
    /// </summary>
    public virtual void OnUnequipped() { }
}
