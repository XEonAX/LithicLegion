using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Shoots bullets, plays a shot and cocks itself back. One emitter per mount: the <see cref="Weapon"/>
/// on the parent decides when this gun's turn comes, so all the gun has to know is how to shoot.
///
/// List the barrels in order and point it at the bullet prefab. Bullets are instantiated unparented,
/// so each one locks in the pose its barrel had at the moment of firing while the gun and barrels
/// carry on moving. The shot is played the same way - through <see cref="AudioManager"/>, from the
/// muzzle, on the gun's own mixer - so it and the bullet leave from the same place.
/// </summary>
public class Gun : WeaponEmitter
{
    [Header("Barrels")]
    [Tooltip("Muzzles to fire from. One bullet leaves each of them per shot, along its own blue Z axis.")]
    [SerializeField] Transform[] barrels;

    [Header("Ammo")]
    [Tooltip("Bullet prefab. It needs a Projectile and a CapsuleCollider.")]
    [SerializeField] Projectile bulletPrefab;

    [Header("Sound")]
    [Tooltip("Played once per shot, from the muzzle. Leave it empty for a silent gun.")]
    [SerializeField] AudioClip fireSound;
    [Tooltip("Bus the shot is played on - the Gun mixer. Leaving it empty sends the shot straight to " +
             "the master, where it cannot be turned down on its own.")]
    [SerializeField] AudioMixerGroup fireOutput;
    [Tooltip("How loud one shot is, before the mixer gets a say.")]
    [Range(0f, 1f)]
    [SerializeField] float fireVolume = 1f;
    [Tooltip("Each shot picks a pitch at random in this range, so a burst does not sound like one " +
             "sample played over and over. Set both ends to 1 to switch the variation off.")]
    [SerializeField] Vector2 firePitch = new Vector2(0.94f, 1.06f);

    [Header("Recoil")]
    [Tooltip("How far the gun cocks back along its barrels, in world units.")]
    [SerializeField] float recoilDistance = 0.15f;
    [Tooltip("Share of the recoil spent cocking back; the rest is the slower return. 0.25 throws the " +
             "gun back over the first quarter of the interval and eases it home over the remaining three.")]
    [Range(0.05f, 0.9f)]
    [SerializeField] float recoilKickPortion = 0.25f;
    [Tooltip("Optional. The part that cocks back. Defaults to this transform - the whole gun.")]
    [SerializeField] Transform recoilPart;

    Transform kick;                 // recoilPart, or this transform when none is set
    Vector3 kickRestLocalPosition;  // where the gun sits when it is not cocked back
    float kickDuration;             // one kick spans one shot interval
    float kickElapsed = -1f;        // negative while the gun is at rest

    void Awake()
    {
        if (barrels == null || barrels.Length == 0)
        {
            Debug.LogWarning($"{name}: Gun has no barrels listed, so it has nothing to fire from.", this);
        }

        kick = recoilPart != null ? recoilPart : transform;
        kickRestLocalPosition = kick.localPosition;
    }

    void Update()
    {
        UpdateRecoil();
    }

    /// <summary>
    /// Fires one bullet from every barrel, then cocks the gun back. <paramref name="interval"/> is the
    /// weapon's shot interval: the recoil spans exactly that long, so the gun is home again well before
    /// the Weapon calls on it next.
    /// </summary>
    public override void Fire(float interval)
    {
        if (bulletPrefab == null)
        {
            Debug.LogError($"{name}: Gun has no bullet prefab to spawn.", this);
            return;
        }

        foreach (Transform barrel in barrels)
        {
            if (barrel == null) continue;

            // Deliberately unparented: the bullet keeps the muzzle's pose and ignores the gun from here on.
            Instantiate(bulletPrefab, barrel.position, barrel.rotation);
        }

        PlayFireSound();

        kickDuration = Mathf.Max(interval, 0f);
        kickElapsed = 0f;
    }

    /// <summary>
    /// Fires the shot sound from the muzzle. Pitched a little differently every time so that the
    /// weapon's own cadence does not turn one sample into an obvious loop.
    /// </summary>
    void PlayFireSound()
    {
        if (fireSound == null) return;

        AudioManager.PlayOneShot(fireSound, MuzzlePosition(), fireOutput, fireVolume,
                                 Random.Range(firePitch.x, firePitch.y));
    }

    Vector3 MuzzlePosition()
    {
        if (barrels != null)
        {
            foreach (Transform barrel in barrels)
            {
                if (barrel != null) return barrel.position;
            }
        }

        return transform.position;
    }

    /// <summary>
    /// Cocks the gun back and returns it to rest across one shot interval: the shot throws it back
    /// quickly, then it eases home over the rest of the interval.
    /// </summary>
    void UpdateRecoil()
    {
        if (kickElapsed < 0f)
        {
            // At rest. Keep re-reading the pose so moving the gun by any other means becomes the new rest.
            kickRestLocalPosition = kick.localPosition;
            return;
        }

        kickElapsed += Time.deltaTime;
        float progress = kickDuration > 0f ? Mathf.Clamp01(kickElapsed / kickDuration) : 1f;

        SetKickOffset(RecoilAmount(progress) * recoilDistance);

        if (progress >= 1f)
        {
            kickElapsed = -1f;
            SetKickOffset(0f);
        }
    }

    /// <summary>
    /// The recoil shape: 0 at the shot, 0 again at rest, peaking after <see cref="recoilKickPortion"/>
    /// of the interval. The cock-back is a decelerating ramp, so the shot spends all its energy
    /// immediately instead of winding up; the return is several times slower and settles. The two
    /// halves meet with zero slope, so the gun hangs at the back for an instant rather than snapping
    /// through a corner.
    /// </summary>
    float RecoilAmount(float progress)
    {
        float kickEnd = Mathf.Clamp(recoilKickPortion, 0.05f, 0.9f);

        if (progress < kickEnd)
        {
            float rising = progress / kickEnd;
            return 1f - Mathf.Pow(1f - rising, 3f);      // ease-out cubic: hard off the mark, then slowing
        }

        float returning = (progress - kickEnd) / (1f - kickEnd);
        return 1f - EaseInOutCubic(returning);           // slower, easing out of the peak into rest
    }

    static float EaseInOutCubic(float u)
    {
        return u < 0.5f
            ? 4f * u * u * u
            : 1f - Mathf.Pow(-2f * u + 2f, 3f) * 0.5f;
    }

    /// <summary>
    /// Slides the gun backwards along its muzzle axis. The offset is built in world space and then
    /// converted through the parent, so it holds up while the gun is being aimed or scaled.
    /// </summary>
    void SetKickOffset(float offset)
    {
        Vector3 restWorld = kick.parent != null
            ? kick.parent.TransformPoint(kickRestLocalPosition)
            : kickRestLocalPosition;

        Vector3 kickedWorld = restWorld - MuzzleDirection() * offset;

        kick.localPosition = kick.parent != null
            ? kick.parent.InverseTransformPoint(kickedWorld)
            : kickedWorld;
    }

    Vector3 MuzzleDirection()
    {
        if (barrels != null)
        {
            foreach (Transform barrel in barrels)
            {
                if (barrel != null) return barrel.forward;
            }
        }

        return transform.forward;
    }

    void OnDrawGizmosSelected()
    {
        if (barrels == null) return;

        Gizmos.color = Color.red;
        foreach (Transform barrel in barrels)
        {
            if (barrel == null) continue;
            Gizmos.DrawLine(barrel.position, barrel.position + barrel.forward * 2f);
        }
    }
}
