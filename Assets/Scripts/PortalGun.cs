using UnityEngine;

/// <summary>
/// One half of the portal-gun pair: hold the trigger and a coloured laser reaches from the muzzle to
/// whatever the gun is aimed at, with a glowing barrel and sparks streaming backwards off it. Release
/// and the beam fades, leaving a small flare where it landed.
///
/// There is no portal yet - the shot is the beam itself. That is deliberate: it keeps this emitter
/// about the thing the reference shows, and leaves a portal system free to be added behind the same
/// release callback later.
///
/// Which mouse button drives this gun is the <see cref="Weapon"/>'s business: the left mount is the
/// blue gun and the right mount is the orange one, so the pair can both be held at once.
/// </summary>
[DisallowMultipleComponent]
public sealed class PortalGun : WeaponEmitter
{
    [Header("Aim")]
    [Tooltip("The muzzle the beam starts from. Defaults to a child called Barrel.")]
    [SerializeField] Transform barrel;
    [Tooltip("Layers the aiming ray can stop on.")]
    [SerializeField] LayerMask hitMask = ~0;
    [Tooltip("Longest beam, in world units, when it hits nothing.")]
    [SerializeField] float maxDistance = 400f;

    [Header("Colour")]
    [Tooltip("HDR beam and glow colour. Above 1 is what the bloom picks up.")]
    [SerializeField] Color beamColor = new Color(0.25f, 0.55f, 3f, 1f);
    [Tooltip("Colour of the blade's own edges while this gun is equipped.")]
    [SerializeField] Color bladeColor = new Color(0.3f, 0.75f, 3f, 1f);

    [Header("Beam")]
    [Tooltip("Additive beam material. Its shader is LithicLegion/Beam.")]
    [SerializeField] Material beamMaterial;
    [Tooltip("Width of the beam's white core.")]
    [SerializeField] float coreWidth = 0.035f;
    [Tooltip("Width of the beam's coloured halo.")]
    [SerializeField] float glowWidth = 0.26f;
    [Tooltip("How long the beam takes to fade once the trigger is released.")]
    [SerializeField] float fadeOutTime = 0.14f;

    [Header("Muzzle")]
    [Tooltip("Additive radial material used for the muzzle and impact glows.")]
    [SerializeField] Material glowMaterial;
    [Tooltip("World size of the barrel glow.")]
    [SerializeField] float muzzleSize = 0.18f;
    [Tooltip("HDR brightness of the barrel glow.")]
    [SerializeField] float muzzleBrightness = 8f;
    [Tooltip("Distance the barrel light reaches.")]
    [SerializeField] float muzzleLightRange = 4f;
    [Tooltip("Strength of the barrel light.")]
    [SerializeField] float muzzleLightPower = 4f;

    [Header("Sparks")]
    [Tooltip("Additive material for the backwards sparks.")]
    [SerializeField] Material sparkMaterial;
    [Tooltip("Sparks per second while the beam is held.")]
    [SerializeField] float sparkRate = 110f;
    [Tooltip("Sparks thrown off the muzzle when the trigger is released.")]
    [SerializeField] float releaseSparks = 50f;
    [Tooltip("How far the spray fans out around the backwards direction.")]
    [SerializeField] float sparkSpread = 4.5f;

    [Header("Blade")]
    [Tooltip("Optional. Found on this object when empty.")]
    [SerializeField] BladeGlow blade;

    readonly RaycastHit[] hits = new RaycastHit[32];

    Weapon weapon;
    Player player;
    Transform weaponRoot;

    BeamLine beam;
    GlowOrb muzzleGlow;
    GlowOrb impactGlow;
    SparkEmitter sparks;

    bool firing;
    float fade;
    float sparkBudget;

    Vector3 endPoint;
    bool endHasHit;

    void Awake()
    {
        Resolve();
        Build();
        ApplyColours();
    }

    void OnEnable()
    {
        Resolve();
        Build();
        ApplyColours();

        if (!firing)
        {
            beam?.Hide();
            muzzleGlow?.Hide();
            impactGlow?.Hide();
        }
    }

    void OnDisable()
    {
        StopEverything();
    }

    void OnDestroy()
    {
        // The effects live under the weapon root so they survive a rig swap; this emitter owns them.
        if (beam != null) Destroy(beam.gameObject);
        if (muzzleGlow != null) Destroy(muzzleGlow.gameObject);
        if (impactGlow != null) Destroy(impactGlow.gameObject);
        if (sparks != null) Destroy(sparks.gameObject);
    }

    void Resolve()
    {
        if (barrel == null) barrel = transform.Find("Barrel");
        if (weapon == null) weapon = GetComponentInParent<Weapon>();
        if (weaponRoot == null) weaponRoot = weapon != null ? weapon.transform : transform;
        if (player == null) player = FindAnyObjectByType<Player>();
        if (blade == null) blade = GetComponent<BladeGlow>();
    }

    void Build()
    {
        if (beam != null || weaponRoot == null) return;

        beam = BeamLine.Create("Portal Beam", weaponRoot, beamMaterial);
        beam.SetWidths(coreWidth, glowWidth);
        beam.SetIntensity(9f, 1.7f);

        muzzleGlow = GlowOrb.Create("Portal Muzzle Glow", weaponRoot, glowMaterial);
        muzzleGlow.Configure(beamColor, muzzleSize, muzzleBrightness, muzzleLightRange, muzzleLightPower);

        impactGlow = GlowOrb.Create("Portal Impact Glow", weaponRoot, glowMaterial);
        impactGlow.Configure(beamColor, muzzleSize * 1.1f, muzzleBrightness * 1.4f, 5f, muzzleLightPower * 0.8f);

        sparks = SparkEmitter.Create("Portal Sparks", weaponRoot, sparkMaterial);
    }

    void ApplyColours()
    {
        if (beam != null) beam.SetColor(beamColor);

        if (muzzleGlow != null)
            muzzleGlow.Configure(beamColor, muzzleSize, muzzleBrightness, muzzleLightRange, muzzleLightPower);

        if (impactGlow != null)
            impactGlow.Configure(beamColor, muzzleSize * 1.1f, muzzleBrightness * 1.4f, 5f, muzzleLightPower * 0.8f);

        if (blade != null) blade.SetColours(bladeColor, bladeColor, 5f, 4f);
    }

    public override void OnTriggerDown()
    {
        Build();
        if (barrel == null || beam == null) return;

        firing = true;
        fade = 1f;
        sparkBudget = 0f;

        Aim();
        ShowBeam();
        muzzleGlow.SetAmount(0.7f);
        if (sparks != null) sparks.Emit(barrel.position, -barrel.forward, SparkTint(), 12, sparkSpread);
    }

    public override void OnTriggerHeld(float deltaTime)
    {
        if (beam == null) Build();
        if (!firing || barrel == null || beam == null || muzzleGlow == null) return;

        Aim();
        ShowBeam();

        // A little noise on the barrel glow keeps a held beam from looking like a static sticker.
        float flicker = 0.72f + 0.28f * Mathf.PerlinNoise(Time.time * 34f, 0.37f);
        muzzleGlow.transform.position = barrel.position;
        muzzleGlow.SetAmount(flicker);

        sparkBudget += sparkRate * deltaTime;
        int count = Mathf.FloorToInt(sparkBudget);
        if (count <= 0) return;

        sparkBudget -= count;
        sparks.Emit(barrel.position, -barrel.forward, SparkTint(), count, sparkSpread);
    }

    public override void OnTriggerUp()
    {
        Build();
        if (!firing) return;

        firing = false;
        muzzleGlow.Hide();

        if (barrel != null && sparks != null)
            sparks.Emit(barrel.position, -barrel.forward, SparkTint(),
                        Mathf.RoundToInt(releaseSparks), sparkSpread * 1.3f);

        if (endHasHit && impactGlow != null)
        {
            impactGlow.Flash(endPoint, beamColor, muzzleSize * 1.1f,
                             muzzleBrightness * 1.5f, 0.28f);
        }

        // Leave the beam where it was and let Update fade it out.
        fade = 1f;
    }

    public override void OnUnequipped()
    {
        StopEverything();
    }

    public override void Fire(float interval)
    {
        // The portal gun is driven by the trigger, not by a cadence.
        OnTriggerDown();
    }

    void StopEverything()
    {
        firing = false;
        fade = 0f;

        if (beam != null) beam.Hide();
        if (muzzleGlow != null) muzzleGlow.Hide();
        if (impactGlow != null) impactGlow.Hide();
    }

    void Update()
    {
        if (firing || fade <= 0f) return;

        fade -= Time.deltaTime / Mathf.Max(0.01f, fadeOutTime);
        beam.SetFade(fade);

        if (fade <= 0f) beam.Hide();
    }

    void ShowBeam()
    {
        if (beam == null) Build();
        if (barrel == null || beam == null || muzzleGlow == null) return;

        Vector3 start = barrel.position;
        beam.Show(start, endPoint);
        beam.SetFade(1f);
        muzzleGlow.transform.position = start;
    }

    /// <summary>
    /// Casts down the bore and keeps the nearest surface that is not the shooter. The beam ends on
    /// that surface, or runs out to <see cref="maxDistance"/> when the shot is fired at the sky.
    /// </summary>
    void Aim()
    {
        if (barrel == null) return;

        Vector3 origin = barrel.position;
        Vector3 direction = barrel.forward;

        int count = Physics.RaycastNonAlloc(origin, direction, hits, maxDistance, hitMask,
                                            QueryTriggerInteraction.Ignore);

        float nearest = float.PositiveInfinity;
        bool found = false;
        RaycastHit best = default;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.distance <= 0f) continue;
            if (IsShooter(hit.collider.transform)) continue;
            if (hit.distance >= nearest) continue;

            nearest = hit.distance;
            best = hit;
            found = true;
        }

        if (found)
        {
            endPoint = best.point;
            endHasHit = true;
        }
        else
        {
            endPoint = origin + direction * maxDistance;
            endHasHit = false;
        }
    }

    bool IsShooter(Transform candidate)
    {
        if (player != null && candidate.IsChildOf(player.transform)) return true;
        if (weaponRoot != null && candidate.IsChildOf(weaponRoot)) return true;
        return false;
    }

    Color SparkTint()
    {
        // A touch of white in the spray keeps it reading as sparks rather than a coloured dust.
        return Color.Lerp(beamColor, Color.white, 0.45f);
    }

    void OnDrawGizmosSelected()
    {
        if (barrel == null) return;

        Gizmos.color = beamColor;
        Gizmos.DrawLine(barrel.position, barrel.position + barrel.forward * 8f);
    }
}
