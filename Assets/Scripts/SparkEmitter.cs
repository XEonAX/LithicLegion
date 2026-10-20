using UnityEngine;

/// <summary>
/// The backwards spray at a portal gun's muzzle: short, stretched sparks that stream away from the
/// beam instead of along it.
///
/// A single world-space <see cref="ParticleSystem"/> is built at runtime with emission and shape
/// switched off. Every spark is emitted explicitly with its own velocity, so the spray always starts
/// from the live muzzle and always points down the bore, no matter how the gun is aimed or scaled.
/// Stretched billboards turn each particle into a streak.
/// </summary>
[DisallowMultipleComponent]
public sealed class SparkEmitter : MonoBehaviour
{
    [Tooltip("Additive radial material; each particle's colour is multiplied into it.")]
    [SerializeField] Material material;
    [Tooltip("Smallest and largest spark, in world units.")]
    [SerializeField] Vector2 size = new Vector2(0.07f, 0.18f);
    [Tooltip("Shortest and longest spark life, in seconds.")]
    [SerializeField] Vector2 lifetime = new Vector2(0.25f, 0.6f);
    [Tooltip("How far a spark is stretched along its velocity.")]
    [SerializeField] float streakScale = 0.12f;
    [Tooltip("How much gravity bends the spray.")]
    [SerializeField] float gravity = 0.1f;
    [Tooltip("Upward kick added to every spark, so the spray rises clear of the muzzle instead of " +
             "disappearing straight back into it.")]
    [SerializeField] float upBias = 0.9f;

    ParticleSystem system;
    ParticleSystemRenderer systemRenderer;

    public static SparkEmitter Create(string name, Transform parent, Material sparkMaterial)
    {
        var holder = new GameObject(name);
        // Built while inactive so Awake sees the material the caller is about to assign.
        holder.SetActive(false);
        holder.transform.SetParent(parent, false);

        var emitter = holder.AddComponent<SparkEmitter>();
        emitter.material = sparkMaterial;
        holder.SetActive(true);
        return emitter;
    }

    void Awake()
    {
        Build();
    }

    void Build()
    {
        if (system != null) return;

        system = gameObject.GetComponent<ParticleSystem>();
        if (system == null) system = gameObject.AddComponent<ParticleSystem>();

        var main = system.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startSpeed = 0f;
        main.startSize = size.y;
        main.startLifetime = lifetime.y;
        main.startColor = Color.white;
        main.maxParticles = 512;
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
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.65f, 0.35f), new GradientAlphaKey(0f, 1f) });
        colour.color = new ParticleSystem.MinMaxGradient(gradient);

        var fade = system.sizeOverLifetime;
        fade.enabled = true;
        fade.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.7f, 0.55f),
            new Keyframe(1f, 0f)));

        systemRenderer = gameObject.GetComponent<ParticleSystemRenderer>();
        if (systemRenderer == null) systemRenderer = gameObject.AddComponent<ParticleSystemRenderer>();
        systemRenderer.sharedMaterial = material;
        systemRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        systemRenderer.velocityScale = streakScale;
        systemRenderer.lengthScale = 2f;
        systemRenderer.alignment = ParticleSystemRenderSpace.View;
        systemRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        systemRenderer.receiveShadows = false;
        systemRenderer.minParticleSize = 0f;
        systemRenderer.maxParticleSize = 0.5f;

        system.Play();
    }

    /// <summary>
    /// Fires <paramref name="count"/> sparks from <paramref name="position"/> along
    /// <paramref name="direction"/>, fanned out by <paramref name="spread"/>.
    /// </summary>
    public void Emit(Vector3 position, Vector3 direction, Color tint, int count, float spread)
    {
        if (count <= 0) return;

        Build();

        for (int i = 0; i < count; i++)
        {
            var emit = new ParticleSystem.EmitParams
            {
                position = position + Random.insideUnitSphere * 0.06f,
                velocity = direction * Random.Range(3.5f, 9f) + Vector3.up * Random.Range(0f, upBias) + Random.insideUnitSphere * spread,
                startColor = tint,
                startSize = Random.Range(size.x, size.y),
                startLifetime = Random.Range(lifetime.x, lifetime.y),
                rotation = Random.Range(0f, 360f),
            };

            system.Emit(emit, 1);
        }
    }
}
