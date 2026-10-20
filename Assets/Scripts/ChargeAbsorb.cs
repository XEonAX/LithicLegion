using UnityEngine;

/// <summary>
/// The rail gun's charge intake: a shell of particles that appears around the muzzle and streams
/// inward. The shell starts small and widens a little as the charge builds; the inward speed and the
/// emission rate ramp up steeply near the end, so the last stretch looks like the gun is pulling
/// energy in rather than just glowing.
///
/// Particles are emitted explicitly rather than by a shape module. Each one is placed on the current
/// shell with its own velocity straight at the centre and a lifetime that lands it there, which lets
/// the shell radius and speed be driven independently every frame.
/// </summary>
[DisallowMultipleComponent]
public sealed class ChargeAbsorb : MonoBehaviour
{
    [Tooltip("Additive particle material. The tint is multiplied into it per particle.")]
    [SerializeField] Material material;
    [Tooltip("Shell radius at the start and at full charge, in metres.")]
    [SerializeField] Vector2 radius = new Vector2(0.30f, 1.2f);
    [Tooltip("Inward speed at the start and at full charge, in metres per second.")]
    [SerializeField] Vector2 inwardSpeed = new Vector2(0.5f, 2.6f);
    [Tooltip("Particles emitted per second at the start and at full charge.")]
    [SerializeField] Vector2 rate = new Vector2(40f, 140f);
    [Tooltip("Particle diameter, in metres.")]
    [SerializeField] float particleSize = 0.09f;
    [Tooltip("Random size variation around the particle size.")]
    [Range(0f, 1f)]
    [SerializeField] float sizeJitter = 0.35f;
    [Tooltip("How sharply the inward speed ramps. 1 is linear; 2+ keeps the early pull slow and " +
             "saves the speed for the last part of the charge.")]
    [SerializeField] float speedPower = 2.2f;
    [Tooltip("How the catchment widens. Below 1 it grows early and then eases; 1 is linear.")]
    [SerializeField] float radiusPower = 1f;
    [Tooltip("How far inward the spawn points cluster. Above 1 keeps most particles off the rim, " +
             "so the catchment has no visible spherical boundary.")]
    [SerializeField] float inwardBias = 1.3f;
    [Tooltip("How sharply the emission rate ramps.")]
    [SerializeField] float ratePower = 1.5f;
    [Tooltip("Hard cap on live particles.")]
    [SerializeField] int maxParticles = 400;
    [Tooltip("Brightness multiplier on the beam colour, so the intake can stay subtler than the shot.")]
    [Range(0f, 1f)]
    [SerializeField] float brightness = 0.9f;

    ParticleSystem system;
    Transform particlesRoot;
    Color tint = Color.white;
    float emitDebt;
    bool emitting;

    /// <summary>Live particles, for verification and tuning.</summary>
    public int LiveParticles => system != null ? system.particleCount : 0;

    public static ChargeAbsorb Create(string name, Transform parent, Material absorbMaterial)
    {
        var holder = new GameObject(name);
        holder.SetActive(false);
        holder.transform.SetParent(parent, false);

        var absorb = holder.AddComponent<ChargeAbsorb>();
        absorb.material = absorbMaterial;
        holder.SetActive(true);
        return absorb;
    }

    void Awake()
    {
        // Built on the first charge instead of on activation: the particle child is parented to the
        // barrel at that point, and nothing exists while the gun is only sitting in the scene.
    }

    void OnDestroy()
    {
        if (particlesRoot != null) Destroy(particlesRoot.gameObject);
    }

    void Build()
    {
        if (system != null) return;

        // The particles live on their own child, simulated in local space. Begin() parents that child
        // to the barrel, so the whole intake follows the gun and the player with no per-frame work.
        var holder = new GameObject("Particles");
        holder.transform.SetParent(transform, false);
        particlesRoot = holder.transform;
        system = holder.AddComponent<ParticleSystem>();

        var main = system.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startSpeed = 0f;
        main.startSize = particleSize;
        main.startLifetime = 1f;
        main.startColor = Color.white;
        main.maxParticles = Mathf.Max(1, maxParticles);
        main.gravityModifier = 0f;

        var emission = system.emission;
        emission.enabled = false;

        var shape = system.shape;
        shape.enabled = false;

        var colour = system.colorOverLifetime;
        colour.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f),
                    new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
        colour.color = new ParticleSystem.MinMaxGradient(gradient);

        var size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.6f, 0.7f),
            new Keyframe(1f, 0.1f)));

        var renderer = holder.GetComponent<ParticleSystemRenderer>();
        if (renderer == null) renderer = holder.AddComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.035f;
        renderer.lengthScale = 2f;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.minParticleSize = 0f;
        renderer.maxParticleSize = 0.5f;

        system.Play();
    }

    /// <summary>
    /// Starts the intake and parents its particles to <paramref name="anchor"/> - the barrel - so the
    /// whole cloud follows the gun and the player for free.
    /// </summary>
    public void Begin(Color beamTint, Transform anchor = null)
    {
        Build();

        if (anchor != null && particlesRoot != null)
        {
            particlesRoot.SetParent(anchor, false);
            particlesRoot.localPosition = Vector3.zero;
            particlesRoot.localRotation = Quaternion.identity;

            // The rig is scaled, and local-space particles inherit that scale. Invert it on the
            // particle child so the Radius and Particle Size fields are world metres, not rig units.
            Vector3 scale = anchor.lossyScale;
            particlesRoot.localScale = new Vector3(
                Mathf.Abs(scale.x) > 0.001f ? 1f / scale.x : 1f,
                Mathf.Abs(scale.y) > 0.001f ? 1f / scale.y : 1f,
                Mathf.Abs(scale.z) > 0.001f ? 1f / scale.z : 1f);
        }

        tint = new Color(beamTint.r * brightness, beamTint.g * brightness, beamTint.b * brightness, 1f);
        emitDebt = 0f;
        emitting = true;
        system.Clear();
    }

    /// <summary>Advances the intake to <paramref name="progress"/> of the charge. Call while charging.</summary>
    public void SetCharge(float progress)
    {
        if (!emitting) return;

        progress = Mathf.Clamp01(progress);

        float shell = Mathf.Lerp(radius.x, radius.y, Mathf.Pow(progress, radiusPower));
        float speed = Mathf.Lerp(inwardSpeed.x, inwardSpeed.y, Mathf.Pow(progress, speedPower));
        float emissions = Mathf.Lerp(rate.x, rate.y, Mathf.Pow(progress, ratePower));

        emitDebt += emissions * Time.deltaTime;
        int count = Mathf.Min((int)emitDebt, 24);
        emitDebt -= count;

        for (int i = 0; i < count; i++)
        {
            Vector3 direction = Random.onUnitSphere;

            // Spawn through the catchment volume, biased inward, and fade the rim out. A shell of
            // particles would read as a geometric sphere; this keeps the boundary invisible.
            float fill = Mathf.Pow(Random.value, Mathf.Max(0.1f, inwardBias));
            float spawnRadius = shell * fill;
            float edge = Mathf.Clamp01((1f - fill) / 0.3f);
            Color particleColour = tint;
            particleColour.a *= edge;

            float travel = spawnRadius / Mathf.Max(0.05f, speed);
            var emit = new ParticleSystem.EmitParams
            {
                position = direction * spawnRadius,
                velocity = -direction * speed,
                startColor = particleColour,
                startSize = particleSize * Random.Range(1f - sizeJitter, 1f + sizeJitter),
                startLifetime = Mathf.Clamp(travel * Random.Range(0.92f, 1.05f), 0.12f, 2f),
                rotation = Random.Range(0f, 360f),
            };

            system.Emit(emit, 1);
        }
    }

    /// <summary>Stops pulling in new particles; the ones already in flight finish their trip.</summary>
    public void Stop()
    {
        emitting = false;
    }

    /// <summary>Stops and removes the particles immediately.</summary>
    public void Hide()
    {
        emitting = false;
        emitDebt = 0f;
        if (system != null) system.Clear();
    }

    void OnDisable()
    {
        Hide();
    }
}
