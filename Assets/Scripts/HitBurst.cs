using UnityEngine;

/// <summary>
/// The rail shot's impact: a short radial spray of particles thrown off the surface it hit, tinted
/// with the same HDR colour as the beam. One <see cref="ParticleSystem"/> is built at runtime and
/// every hit emits explicitly, so there is nothing to pool or spawn.
/// </summary>
[DisallowMultipleComponent]
public sealed class HitBurst : MonoBehaviour
{
    [Tooltip("Additive particle material. The hit colour is multiplied into it.")]
    [SerializeField] Material material;
    [Tooltip("Radius of the impact sphere, in metres. 5 is a 10 m sphere across.")]
    [SerializeField] float explosionRadius = 5f;
    [Tooltip("Smallest and largest particle, in world units.")]
    [SerializeField] Vector2 size = new Vector2(1.5f, 3.0f);
    [Tooltip("Shortest and longest particle life, in seconds.")]
    [SerializeField] Vector2 lifetime = new Vector2(2.2f, 3.0f);
    [Tooltip("How far particles drift outward while they linger, in metres per second.")]
    [SerializeField] float driftSpeed = 0.2f;
    [Tooltip("Brightness multiplier on the beam colour for the blast.")]
    [SerializeField] float brightness = 0.9f;
    [Tooltip("How much of the lifetime holds at full strength before the fade begins.")]
    [Range(0f, 0.95f)]
    [SerializeField] float holdFraction = 0.65f;
    [Tooltip("How much gravity bends the cloud.")]
    [SerializeField] float gravity = 0f;
    [Tooltip("Hard cap on live impact particles.")]
    [SerializeField] int maxParticles = 500;

    [Header("Streaks")]
    [Tooltip("Fast, stretched particles thrown out at the moment of impact.")]
    [SerializeField] int streakCount = 48;
    [Tooltip("Smallest and largest streak, in world units.")]
    [SerializeField] Vector2 streakSize = new Vector2(0.12f, 0.28f);
    [Tooltip("Shortest and longest streak life, in seconds.")]
    [SerializeField] Vector2 streakLifetime = new Vector2(0.25f, 0.5f);
    [Tooltip("Fastest streak, in metres per second.")]
    [SerializeField] float streakSpeed = 20f;
    [Tooltip("How far a streak is stretched along its velocity.")]
    [SerializeField] float streakStretch = 0.06f;

    ParticleSystem system;
    ParticleSystem streaks;
    Transform particlesRoot;

    public static HitBurst Create(string name, Transform parent, Material burstMaterial)
    {
        var holder = new GameObject(name);
        holder.SetActive(false);
        holder.transform.SetParent(parent, false);

        var burst = holder.AddComponent<HitBurst>();
        burst.material = burstMaterial;
        holder.SetActive(true);
        return burst;
    }

    void Awake()
    {
        // Built on the first impact so nothing is created while the rig is only sitting in the scene.
    }

    void OnDestroy()
    {
        if (particlesRoot != null) Destroy(particlesRoot.gameObject);
    }

    void Build()
    {
        if (system != null) return;

        var holder = new GameObject("Particles");
        holder.transform.SetParent(transform, false);
        particlesRoot = holder.transform;

        // The rig is scaled; the particle sizes are world metres, so undo the parent scale.
        Vector3 scale = transform.lossyScale;
        particlesRoot.localScale = new Vector3(
            Mathf.Abs(scale.x) > 0.001f ? 1f / scale.x : 1f,
            Mathf.Abs(scale.y) > 0.001f ? 1f / scale.y : 1f,
            Mathf.Abs(scale.z) > 0.001f ? 1f / scale.z : 1f);

        system = holder.AddComponent<ParticleSystem>();

        var main = system.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startSpeed = 0f;
        main.startSize = size.y;
        main.startLifetime = lifetime.y;
        main.startColor = Color.white;
        main.maxParticles = Mathf.Max(1, maxParticles);
        main.gravityModifier = gravity;

        var emission = system.emission;
        emission.enabled = false;
        var shape = system.shape;
        shape.enabled = false;

        var colour = system.colorOverLifetime;
        colour.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, holdFraction), new GradientAlphaKey(0f, 1f) });
        colour.color = new ParticleSystem.MinMaxGradient(gradient);

        var fade = system.sizeOverLifetime;
        fade.enabled = true;
        fade.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.25f),
            new Keyframe(0.12f, 1f),
            new Keyframe(holdFraction, 1f),
            new Keyframe(1f, 0.4f)));

        var renderer = holder.GetComponent<ParticleSystemRenderer>();
        if (renderer == null) renderer = holder.AddComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.minParticleSize = 0f;
        renderer.maxParticleSize = 8f;

        system.Play();
        BuildStreaks();
    }

    void BuildStreaks()
    {
        var holder = new GameObject("Streaks");
        holder.transform.SetParent(particlesRoot, false);

        streaks = holder.AddComponent<ParticleSystem>();
        var main = streaks.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startSpeed = 0f;
        main.startSize = streakSize.y;
        main.startLifetime = streakLifetime.y;
        main.startColor = Color.white;
        main.maxParticles = 300;
        main.gravityModifier = 0f;

        var emission = streaks.emission;
        emission.enabled = false;
        var shape = streaks.shape;
        shape.enabled = false;

        var colour = streaks.colorOverLifetime;
        colour.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.35f), new GradientAlphaKey(0f, 1f) });
        colour.color = new ParticleSystem.MinMaxGradient(gradient);

        var fade = streaks.sizeOverLifetime;
        fade.enabled = true;
        fade.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(1f, 0f)));

        var renderer = holder.GetComponent<ParticleSystemRenderer>();
        if (renderer == null) renderer = holder.AddComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = streakStretch;
        renderer.lengthScale = 2f;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.maxParticleSize = 8f;

        streaks.Play();
    }

    /// <summary>Throws <paramref name="count"/> particles off an impact at <paramref name="point"/>.</summary>
    public void Emit(Vector3 point, Vector3 normal, Color tint, int count)
    {
        if (count <= 0) return;

        Build();
        Color blastTint = tint * brightness;
        blastTint.a = 1f;

        for (int i = 0; i < count; i++)
        {
            // Fill a sphere, biased toward the outer half, so the blast reads as a huge ball of
            // particles rather than a spray from one point.
            Vector3 direction = Random.onUnitSphere;
            float radius = explosionRadius * Mathf.Pow(Random.value, 0.4f);
            Vector3 drift = direction * Random.Range(0f, driftSpeed);

            var emit = new ParticleSystem.EmitParams
            {
                position = point + direction * radius,
                velocity = drift,
                startColor = blastTint,
                startSize = Random.Range(size.x, size.y),
                startLifetime = Random.Range(lifetime.x, lifetime.y),
                rotation = Random.Range(0f, 360f),
            };

            system.Emit(emit, 1);
        }

        EmitStreaks(point, tint);
    }

    void EmitStreaks(Vector3 point, Color tint)
    {
        if (streakCount <= 0) return;

        Color streakTint = Color.Lerp(tint, Color.white, 0.12f) * (brightness * 0.9f);
        streakTint.a = 1f;
        for (int i = 0; i < streakCount; i++)
        {
            Vector3 direction = Random.onUnitSphere;
            var emit = new ParticleSystem.EmitParams
            {
                position = point + direction * Random.Range(0f, 0.3f),
                velocity = direction * Random.Range(streakSpeed * 0.4f, streakSpeed),
                startColor = streakTint,
                startSize = Random.Range(streakSize.x, streakSize.y),
                startLifetime = Random.Range(streakLifetime.x, streakLifetime.y),
                rotation = Random.Range(0f, 360f),
            };

            streaks.Emit(emit, 1);
        }
    }
}
