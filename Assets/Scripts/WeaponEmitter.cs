using UnityEngine;

/// <summary>
/// Something a <see cref="Weapon"/> can mount and drive: one piece that knows how to shoot.
///
/// <see cref="Gun"/> is the bullet version. A rail gun or a laser would be other subclasses, so the
/// Weapon that orchestrates them never has to know which kind it is holding.
/// </summary>
public abstract class WeaponEmitter : MonoBehaviour
{
    /// <summary>Which mount this emitter sits on, so a Weapon can alternate between the two.</summary>
    public enum Side { Left, Right }

    [Tooltip("Which mount this is. A Weapon pairs a Left with a Right and alternates between them.")]
    [SerializeField] Side side = Side.Right;

    public Side EmitterSide => side;

    /// <summary>
    /// Fire whatever this emitter fires. <paramref name="interval"/> is the weapon's shot interval, so
    /// an emitter can time its own feedback - recoil, charge-up, beam - to the cadence driving it.
    /// </summary>
    public abstract void Fire(float interval);
}
