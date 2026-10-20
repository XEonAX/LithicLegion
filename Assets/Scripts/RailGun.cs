using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// The rail gun. Holding the trigger winds it up with a rising whine and a matching camera shake; at
/// full charge it fires one hitscan shot whose entire visible effect is a short-lived, shadow-casting
/// muzzle orb plus a field-of-view kick and a camera jerk.
///
/// The whine and the shot are two halves of one recorded take, split where the blast begins. The
/// charge clip runs from the last 3.1 s of the take's build-up up to the quiet dip before the
/// transient, so it can be stopped cleanly; the fire clip starts on that dip, and the muzzle flash,
/// light and camera punch wait <see cref="blastDelay"/> so they land on the bang rather than on the
/// clip's lead-in. Holding the trigger for the length of the charge clip is what fires the shot.
///
/// Nothing glows on the gun while charging - the reference deliberately leaves the charge feedback to
/// the camera. The blade keeps whatever glow it is authored with; this script never brightens it.
/// </summary>
[DisallowMultipleComponent]
public sealed class RailGun : WeaponEmitter
{
    [Header("Aim")]
    [Tooltip("The muzzle the shot's flare sits on. Defaults to a child called Barrel.")]
    [SerializeField] Transform barrel;

    [Header("Charge")]
    [Tooltip("How long the trigger must be held before the shot fires, in seconds. Ignored while " +
             "Charge Matches Sound is on and a charge sound is assigned.")]
    [SerializeField] float chargeTime = 1.65f;
    [Tooltip("Take the charge length from the charge clip, so the shot never cuts the whine off " +
             "early or leaves a gap of silence after it ends.")]
    [SerializeField] bool chargeMatchesSound = true;

    [Header("Sound")]
    [Tooltip("Charge-up whine. Started on trigger down and stopped if the shot is cancelled.")]
    [SerializeField] AudioClip chargeSound;
    [Tooltip("Blast and its tail, played at the muzzle when the shot commits.")]
    [SerializeField] AudioClip fireSound;
    [Tooltip("Bus both sounds play on - the Gun mixer. Leaving it empty sends them to the master.")]
    [SerializeField] AudioMixerGroup soundOutput;
    [Tooltip("How loud the charge is, before the mixer gets a say.")]
    [Range(0f, 1f)]
    [SerializeField] float chargeVolume = 0.5f;
    [Tooltip("How loud the blast is, before the mixer gets a say.")]
    [Range(0f, 1f)]
    [SerializeField] float fireVolume = 0.9f;
    [Tooltip("How much of the whine and blast is placed in the world. The player's own gun is heard " +
             "from the third-person camera, so 1 would fade it by the camera distance; a little blend " +
             "keeps a hint of the muzzle without losing the volume.")]
    [Range(0f, 1f)]
    [SerializeField] float spatialBlend = 0.1f;
    [Tooltip("How far into the fire clip its blast transient sits. The muzzle flash, light and camera " +
             "punch wait this long so they land on the bang. Measured at 0.037 s from the source take.")]
    [SerializeField] float blastDelay = 0.037f;

    [Header("Colour")]
    [Tooltip("HDR colour of the shot's flare and light.")]
    [SerializeField] Color beamColor = new Color(0.65f, 2.6f, 2.8f, 1f);

    [Header("Flash")]
    [Tooltip("Additive radial material used for the muzzle flare.")]
    [SerializeField] Material glowMaterial;
    [Tooltip("World size of the flare, including its halo.")]
    [SerializeField] float muzzleFlashSize = 2.4f;
    [Tooltip("HDR brightness of the flare's core.")]
    [SerializeField] float muzzleFlashBrightness = 9f;
    [Tooltip("Distance the flare's light reaches.")]
    [SerializeField] float muzzleLightRange = 10f;
    [Tooltip("Strength of the flare's light. Its shadow is what puts the sphere's silhouette on the floor.")]
    [SerializeField] float muzzleLightPower = 18f;
    [Tooltip("How long the flare is visible, in seconds. The reference keeps it short.")]
    [SerializeField] float flashDuration = 0.24f;

    [Header("Charge intake")]
    [Tooltip("Additive particle material for the shell pulled into the muzzle while charging. The " +
             "beam colour is multiplied into it.")]
    [SerializeField] Material chargeAbsorbMaterial;

    [Header("Shot trace")]
    [Tooltip("Additive beam material. Its shader is LithicLegion/Beam.")]
    [SerializeField] Material beamMaterial;
    [Tooltip("Width of the beam's white core.")]
    [SerializeField] float beamCoreWidth = 0.05f;
    [Tooltip("Width of the beam's coloured halo.")]
    [SerializeField] float beamGlowWidth = 0.34f;
    [Tooltip("HDR brightness of the beam's core.")]
    [SerializeField] float beamCoreIntensity = 9f;
    [Tooltip("HDR brightness of the beam's halo.")]
    [SerializeField] float beamGlowIntensity = 1.8f;
    [Tooltip("How long the shot's beam is visible, in seconds.")]
    [SerializeField] float beamDuration = 0.18f;
    [Tooltip("Layers the shot ray can stop on.")]
    [SerializeField] LayerMask hitMask = ~0;
    [Tooltip("Longest trace when the shot hits nothing.")]
    [SerializeField] float maxHitDistance = 400f;
    [Tooltip("Additive material for the impact particles.")]
    [SerializeField] Material hitBurstMaterial;
    [Tooltip("Particles thrown off the impact.")]
    [SerializeField] int hitBurstCount = 220;
    [Tooltip("World size of the impact flash.")]
    [SerializeField] float hitFlashSize = 1.1f;
    [Tooltip("HDR brightness of the impact flash.")]
    [SerializeField] float hitFlashBrightness = 14f;
    [Tooltip("How long the impact flash lasts, in seconds.")]
    [SerializeField] float hitFlashDuration = 0.2f;
    [Tooltip("Distance the impact light reaches.")]
    [SerializeField] float hitLightRange = 8f;
    [Tooltip("Strength of the impact light.")]
    [SerializeField] float hitLightPower = 10f;

    [Header("Explosion force")]
    [Tooltip("Impulse applied to each non-kinematic rigidbody at the blast centre.")]
    [SerializeField] float explosionForce = 60f;
    [Tooltip("Physical blast radius, in metres. Match the visual sphere's radius (5 = 10 m across).")]
    [SerializeField] float explosionForceRadius = 5f;
    [Tooltip("Upward bias added to the blast, as a fraction of the radius.")]
    [SerializeField] float explosionUpwards = 0.4f;
    [Tooltip("Layers the blast can push.")]
    [SerializeField] LayerMask explosionMask = ~0;

    Weapon weapon;
    Player player;
    Transform weaponRoot;
    GlowSphere muzzleGlow;
    ChargeAbsorb chargeAbsorb;

    BeamLine shotBeam;
    HitBurst hitBurst;
    GlowSphere hitGlow;
    float beamTimer;

    readonly RaycastHit[] hits = new RaycastHit[32];
    readonly Collider[] explosionHits = new Collider[128];
    readonly HashSet<Rigidbody> explosionBodies = new HashSet<Rigidbody>();

    AudioManager.Voice chargeVoice;

    bool charging;
    bool spent;
    float charge;

    bool blastPending;
    float blastTime;

    /// <summary>The hold the charge is actually paced against: the clip's own length when it has one.</summary>
    float ChargeDuration => chargeMatchesSound && chargeSound != null
        ? Mathf.Max(0.05f, chargeSound.length)
        : Mathf.Max(0.05f, chargeTime);

    void Awake()
    {
        Resolve();
        Build();
    }

    void OnEnable()
    {
        Resolve();
        Build();
        if (muzzleGlow != null) muzzleGlow.Hide();
        if (chargeAbsorb != null) chargeAbsorb.Hide();
        if (shotBeam != null) shotBeam.Hide();
        if (hitGlow != null) hitGlow.Hide();
        beamTimer = 0f;
    }

    void Update()
    {
        // The whine lives at the muzzle, which moves with the player and the aim.
        if (chargeVoice != null && barrel != null) chargeVoice.SetPosition(barrel.position);

        if (beamTimer > 0f)
        {
            beamTimer = Mathf.Max(0f, beamTimer - Time.deltaTime);
            if (shotBeam != null)
            {
                shotBeam.SetFade(beamTimer / Mathf.Max(0.01f, beamDuration));
                if (beamTimer <= 0f) shotBeam.Hide();
            }
        }

        // The intake keeps pulling at full strength through the fire clip's short lead-in, so it is
        // still there when the blast lands rather than stopping a frame before it.
        if (blastPending)
        {
            if (chargeAbsorb != null && barrel != null) chargeAbsorb.SetCharge(1f);
            if (Time.time >= blastTime) Blast();
        }
    }

    void OnDisable()
    {
        CancelCharge();
        CancelBlast();
        if (chargeAbsorb != null) chargeAbsorb.Hide();
        if (shotBeam != null) shotBeam.Hide();
        if (hitGlow != null) hitGlow.Hide();
        beamTimer = 0f;
    }

    void OnDestroy()
    {
        StopChargeSound();
        if (muzzleGlow != null) Destroy(muzzleGlow.gameObject);
        if (chargeAbsorb != null && chargeAbsorb.gameObject != gameObject) Destroy(chargeAbsorb.gameObject);
        if (shotBeam != null) Destroy(shotBeam.gameObject);
        if (hitBurst != null && hitBurst.gameObject != gameObject) Destroy(hitBurst.gameObject);
        if (hitGlow != null) Destroy(hitGlow.gameObject);
    }

    void Resolve()
    {
        if (barrel == null) barrel = transform.Find("Barrel");
        if (weapon == null) weapon = GetComponentInParent<Weapon>();
        if (player == null) player = GetComponentInParent<Player>();
        if (weaponRoot == null) weaponRoot = weapon != null ? weapon.transform : transform;
    }

    void Build()
    {
        if (weaponRoot == null) return;

        if (muzzleGlow == null)
        {
            muzzleGlow = GlowSphere.Create("Rail Muzzle Glow", weaponRoot, glowMaterial);
            ConfigureMuzzle();
            muzzleGlow.Hide();
        }

        if (chargeAbsorb == null)
        {
            // Prefer the component authored on the rig so its settings are tunable in the Inspector;
            // fall back to building one so an older rig still gets the effect.
            chargeAbsorb = GetComponentInChildren<ChargeAbsorb>(true);
            if (chargeAbsorb == null)
            {
                chargeAbsorb = ChargeAbsorb.Create("Rail Charge Intake", weaponRoot, chargeAbsorbMaterial);
            }

            chargeAbsorb.Hide();
        }

        if (shotBeam == null)
        {
            shotBeam = BeamLine.Create("Rail Beam", weaponRoot, beamMaterial);
            shotBeam.Hide();
        }

        if (hitBurst == null)
        {
            // Prefer the authored component so the blast is tunable in the Inspector.
            hitBurst = GetComponentInChildren<HitBurst>(true);
            if (hitBurst == null)
            {
                hitBurst = HitBurst.Create("Rail Hit Burst", weaponRoot, hitBurstMaterial);
            }
        }

        if (hitGlow == null)
        {
            hitGlow = GlowSphere.Create("Rail Hit Glow", weaponRoot, glowMaterial);
            hitGlow.Configure(beamColor, hitLightRange, hitLightPower, LightShadows.None);
            hitGlow.Hide();
        }
    }

    void ConfigureMuzzle()
    {
        if (muzzleGlow == null) return;

        muzzleGlow.Configure(beamColor, muzzleLightRange, muzzleLightPower, LightShadows.Hard);
    }

    public override void OnTriggerDown()
    {
        Build();
        if (charging || spent || muzzleGlow == null) return;

        // Charging is whine and shake and nothing else. Clear any flare left from the last shot.
        muzzleGlow.Hide();
        charging = true;
        charge = 0f;

        StartChargeSound();
        if (chargeAbsorb != null && barrel != null) chargeAbsorb.Begin(beamColor, barrel);
        if (CameraFx.Instance != null) CameraFx.Instance.SetCharge(0.05f);
    }

    public override void OnTriggerHeld(float deltaTime)
    {
        if (!charging) return;

        if (chargeSound != null && chargeVoice != null && chargeVoice.IsPlaying)
        {
            // The whine is the clock. Audio playback can start a beat after Play() while the device
            // spins up, and a frame hitch can stretch the frame clock; following the clip means the
            // shot commits exactly when the build-up ends instead of cutting it off or leaving a gap.
            charge = Mathf.Clamp01(chargeVoice.PlaybackTime / Mathf.Max(0.01f, chargeSound.length));
        }
        else
        {
            charge = Mathf.Clamp01(charge + deltaTime / ChargeDuration);
        }

        if (chargeAbsorb != null && barrel != null) chargeAbsorb.SetCharge(charge);
        if (CameraFx.Instance != null) CameraFx.Instance.SetCharge(charge);

        if (charge >= 1f) FireShot();
    }

    public override void OnTriggerUp()
    {
        if (charging) CancelCharge();
        spent = false;
    }

    public override void OnUnequipped()
    {
        CancelCharge();
        CancelBlast();
        if (chargeAbsorb != null) chargeAbsorb.Hide();
        if (shotBeam != null) shotBeam.Hide();
        if (hitGlow != null) hitGlow.Hide();
        beamTimer = 0f;
        spent = false;
    }

    public override void Fire(float interval)
    {
        OnTriggerDown();
    }

    void FireShot()
    {
        charging = false;
        spent = true;
        charge = 0f;

        // The take's charge ends on a quiet dip exactly where the blast begins, so stopping here is
        // silent and the fire clip starts the bang without a seam.
        StopChargeSound();
        if (CameraFx.Instance != null) CameraFx.Instance.SetCharge(0f);

        PlayFireSound();

        blastPending = true;
        blastTime = Time.time + Mathf.Max(0f, blastDelay);
        if (blastDelay <= 0f) Blast();
    }

    /// <summary>The moment the sound reaches its bang: the flash, its light and the camera's kick.</summary>
    void Blast()
    {
        blastPending = false;
        if (chargeAbsorb != null) chargeAbsorb.Stop();
        if (CameraFx.Instance != null) CameraFx.Instance.Punch(1f);

        if (barrel == null || muzzleGlow == null) return;

        Vector3 origin = barrel.position;
        muzzleGlow.Flash(origin, beamColor, muzzleFlashSize, muzzleFlashBrightness, flashDuration);
        TraceShot(origin);
    }

    /// <summary>
    /// Draws the hitscan line and, if it lands on something, throws the impact burst and flash at the
    /// hit point. Runs on the blast frame, so the trace and the bang are the same event.
    /// </summary>
    void TraceShot(Vector3 origin)
    {
        if (shotBeam == null || barrel == null) return;

        Vector3 direction = barrel.forward;
        bool hitSomething = TryHitShot(origin, direction, out Vector3 end, out Vector3 normal);

        shotBeam.SetColor(beamColor);
        shotBeam.SetWidths(beamCoreWidth, beamGlowWidth);
        shotBeam.SetIntensity(beamCoreIntensity, beamGlowIntensity);
        shotBeam.Show(origin, end);
        shotBeam.SetFade(1f);
        beamTimer = beamDuration;

        if (!hitSomething) return;

        if (hitBurst != null) hitBurst.Emit(end, normal, beamColor, hitBurstCount);
        if (hitGlow != null) hitGlow.Flash(end, beamColor, hitFlashSize, hitFlashBrightness, hitFlashDuration);
        ApplyExplosionForce(end);
    }

    /// <summary>
    /// Pushes every non-kinematic rigidbody inside the blast sphere. Static geometry has no rigidbody
    /// and kinematic bodies ignore the impulse, so only loose objects are thrown.
    /// </summary>
    void ApplyExplosionForce(Vector3 centre)
    {
        int count = Physics.OverlapSphereNonAlloc(centre, explosionForceRadius, explosionHits, explosionMask,
                                                  QueryTriggerInteraction.Ignore);
        explosionBodies.Clear();

        for (int i = 0; i < count; i++)
        {
            Collider collider = explosionHits[i];
            if (collider == null) continue;

            Rigidbody body = collider.attachedRigidbody;
            if (body == null || body.isKinematic) continue;
            if (!explosionBodies.Add(body)) continue;

            body.AddExplosionForce(explosionForce, centre, explosionForceRadius, explosionUpwards,
                                   ForceMode.Impulse);
        }
    }

    bool TryHitShot(Vector3 origin, Vector3 direction, out Vector3 point, out Vector3 normal)
    {
        point = origin + direction * maxHitDistance;
        normal = -direction;

        int count = Physics.RaycastNonAlloc(origin, direction, hits, maxHitDistance, hitMask,
                                            QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.distance <= 0f) continue;
            if (IsShooter(hit.collider.transform)) continue;
            if (hit.distance >= nearest) continue;

            nearest = hit.distance;
            point = hit.point;
            normal = hit.normal;
            found = true;
        }

        return found;
    }

    bool IsShooter(Transform candidate)
    {
        if (player != null && (candidate == player.transform || candidate.IsChildOf(player.transform))) return true;
        if (weaponRoot != null && (candidate == weaponRoot || candidate.IsChildOf(weaponRoot))) return true;
        return false;
    }

    void StartChargeSound()
    {
        StopChargeSound();
        if (chargeSound == null || barrel == null) return;

        chargeVoice = AudioManager.Play(chargeSound, barrel.position, soundOutput, chargeVolume,
                                        spatialBlend: spatialBlend);
    }

    void StopChargeSound()
    {
        if (chargeVoice == null) return;

        chargeVoice.Stop();
        chargeVoice = null;
    }

    void PlayFireSound()
    {
        if (fireSound == null || barrel == null) return;

        AudioManager.PlayOneShot(fireSound, barrel.position, soundOutput, fireVolume,
                                 spatialBlend: spatialBlend);
    }

    void CancelBlast()
    {
        blastPending = false;
    }

    void CancelCharge()
    {
        charging = false;
        charge = 0f;

        StopChargeSound();
        if (chargeAbsorb != null) chargeAbsorb.Stop();
        if (muzzleGlow != null) muzzleGlow.Hide();
        if (CameraFx.Instance != null) CameraFx.Instance.SetCharge(0f);
    }
}
