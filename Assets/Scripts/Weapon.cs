using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Orchestrates the emitters mounted on a character: reads the fire input, paces the shots, and
/// alternates the left and right mounts so the two never fire on the same frame.
///
/// Put this on the parent that holds the mounts - the object Player aims - and drop the left and right
/// emitters into their slots. A weapon with only one emitter fitted simply fires it every interval
/// instead of alternating, so a lone gun still works.
/// </summary>
public class Weapon : MonoBehaviour
{
    [Header("Mounts")]
    [Tooltip("Emitter on the left mount: a Gun today, a rail gun or laser later.")]
    [SerializeField] WeaponEmitter left;
    [Tooltip("Emitter on the right mount.")]
    [SerializeField] WeaponEmitter right;

    [Header("Fire")]
    [Tooltip("Shots per second for the weapon as a whole. Two emitters alternating means each one fires " +
             "half as often as this, and this is also the interval each emitter's recoil spans.")]
    [SerializeField] float fireRate = 5f;

    InputAction fireAction;
    float nextShotTime;

    // Which mount is due next. Kept across trigger pulls on purpose: tapping repeatedly keeps
    // alternating rather than firing the same gun every time.
    bool leftIsNext = true;

    float Interval => 1f / Mathf.Max(fireRate, 0.01f);

    void Awake()
    {
        // The same project-wide actions asset that Player and Look read their actions from.
        fireAction = InputSystem.actions.FindAction("Attack");
        if (fireAction == null)
        {
            Debug.LogError($"{name}: no 'Attack' action in the project-wide input actions.", this);
        }

        CheckMount(left, WeaponEmitter.Side.Left, "Left");
        CheckMount(right, WeaponEmitter.Side.Right, "Right");

        if (left == null && right == null)
        {
            Debug.LogWarning($"{name}: Weapon has no emitters assigned, so it has nothing to fire.", this);
        }
    }

    /// <summary>Catches an emitter dropped into the wrong slot, which would read as inverted fire order.</summary>
    void CheckMount(WeaponEmitter emitter, WeaponEmitter.Side expected, string slot)
    {
        if (emitter != null && emitter.EmitterSide != expected)
        {
            Debug.LogWarning($"{name}: the {slot} slot holds a {emitter.EmitterSide} emitter " +
                             $"({emitter.name}). Swap the slots, or change that emitter's Side.", this);
        }
    }

    void Update()
    {
        if (fireAction == null || !fireAction.IsPressed()) return;
        if (Time.time < nextShotTime) return;

        nextShotTime = Time.time + Interval;
        Fire();
    }

    /// <summary>Fires whichever mount is due, and hands the next shot to the other one.</summary>
    public void Fire()
    {
        WeaponEmitter emitter = NextEmitter();
        if (emitter == null) return;

        emitter.Fire(Interval);
        leftIsNext = !leftIsNext;
    }

    /// <summary>
    /// The emitter due to fire. When one slot is empty this falls back to the other mount, so a lone
    /// gun keeps firing every interval instead of skipping every other shot.
    /// </summary>
    WeaponEmitter NextEmitter()
    {
        WeaponEmitter due = leftIsNext ? left : right;
        WeaponEmitter other = leftIsNext ? right : left;

        return due != null ? due : other;
    }
}
