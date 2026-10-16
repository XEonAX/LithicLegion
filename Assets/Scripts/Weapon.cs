using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Orchestrates the emitters mounted on a character: reads the fire input, paces the shots, and
/// alternates the left and right mounts so the two never fire on the same frame.
///
/// Each mount holds a list. Every gun on the mount that is due fires together as one volley, so a
/// triple mount puts three bullets in the air per shot while the mount as a whole still only fires
/// once per interval. A weapon with only one mount fitted simply fires it every interval instead of
/// alternating, so a lone gun still works.
///
/// Put this on the parent that holds the mounts - the object Player aims - and drop each gun into the
/// mount matching its own Side.
/// </summary>
public class Weapon : MonoBehaviour
{
    [Header("Mounts")]
    [Tooltip("Emitters on the left mount. All of them fire together when the left mount's turn comes.")]
    [SerializeField] WeaponEmitter[] left;
    [Tooltip("Emitters on the right mount. All of them fire together on the right mount's turn.")]
    [SerializeField] WeaponEmitter[] right;

    [Header("Fire")]
    [Tooltip("Shots per second for the weapon as a whole. The mounts alternate, so each mount fires half " +
             "as often as this, and this is also the interval every gun's recoil spans.")]
    [SerializeField] float fireRate = 5f;

    InputAction fireAction;
    float nextShotTime;

    // Which mount is due next. Kept across trigger pulls on purpose: tapping repeatedly keeps
    // alternating rather than firing the same mount every time.
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

        if (!HasEmitters(left) && !HasEmitters(right))
        {
            Debug.LogWarning($"{name}: Weapon has no emitters assigned, so it has nothing to fire.", this);
        }
    }

    /// <summary>Catches an emitter dropped into the wrong mount, which would read as inverted fire order.</summary>
    void CheckMount(WeaponEmitter[] mount, WeaponEmitter.Side expected, string slot)
    {
        if (mount == null) return;

        foreach (WeaponEmitter emitter in mount)
        {
            if (emitter == null || emitter.EmitterSide == expected) continue;

            Debug.LogWarning($"{name}: the {slot} mount holds a {emitter.EmitterSide} emitter " +
                             $"({emitter.name}). Move it to the other mount, or change that emitter's Side.", this);
        }
    }

    void Update()
    {
        if (fireAction == null || !fireAction.IsPressed()) return;
        if (Time.time < nextShotTime) return;

        nextShotTime = Time.time + Interval;
        Fire();
    }

    /// <summary>Fires whichever mount is due - every gun on it - and hands the next shot to the other.</summary>
    public void Fire()
    {
        WeaponEmitter[] due = leftIsNext ? left : right;

        // A mount with nothing fitted falls back to the other one, so a one-sided weapon keeps firing
        // every interval instead of skipping every other shot.
        if (!HasEmitters(due)) due = leftIsNext ? right : left;
        if (!HasEmitters(due)) return;

        foreach (WeaponEmitter emitter in due)
        {
            if (emitter != null) emitter.Fire(Interval);
        }

        leftIsNext = !leftIsNext;
    }

    static bool HasEmitters(WeaponEmitter[] mount)
    {
        if (mount == null) return false;

        foreach (WeaponEmitter emitter in mount)
        {
            if (emitter != null) return true;
        }

        return false;
    }
}
