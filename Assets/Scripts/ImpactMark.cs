using UnityEngine;

/// <summary>
/// The mark a bullet leaves where it stopped: a shallow domed disc of emissive material, laid flat on
/// the surface and pointing along the surface normal, which darkens and sinks out of sight.
///
/// Pooled through <see cref="ObjectPool"/> - rent one, call <see cref="Show"/>, and it returns itself
/// when the fade finishes. The prefab's own scale is the mark's size.
///
/// The mark is not parented to what it hit. Instead the hit is remembered in that object's local
/// space and the pose is rebuilt from it every frame, so a mark on a moving object rides along with
/// it while the pooled instance stays put under the pool. That keeps reparenting - and the transform
/// churn that comes with it - out of a spawn path that can run several times a second.
/// </summary>
[DisallowMultipleComponent]
public sealed class ImpactMark : PooledEffect
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [Header("Placement")]
    [Tooltip("Lift off the surface, in world units, so the disc's rim does not fight the surface for " +
             "the same pixels.")]
    [SerializeField] float skin = 0.004f;
    [Tooltip("How far the mark travels into the surface over its life. This has to clear the disc's " +
             "own height plus the skin, or the rim is still showing when the mark is retired.")]
    [SerializeField] float sinkDepth = 0.09f;

    [Header("Timing")]
    [Tooltip("How long the mark lasts, in seconds.")]
    [SerializeField] float lifetime = 1.6f;
    [Tooltip("Share of the life spent at full brightness before the fade starts. Gives the hit a beat " +
             "to register before it starts going away.")]
    [Range(0f, 0.9f)]
    [SerializeField] float hold = 0.25f;

    [Header("Colour")]
    [Tooltip("Tint at the moment of impact. Over 1 is HDR, which is what the scene's bloom picks up.")]
    [SerializeField] Color startTint = new Color(0.55f, 2.4f, 5.5f, 1f);
    [Tooltip("Tint at the end of the life. The material blends additively, so black is invisible - the " +
             "mark is gone from both ends at once, faded out and sunk in.")]
    [SerializeField] Color endTint = Color.black;

    [Tooltip("The mesh to tint. Leave empty to use the first MeshRenderer on this object or below it.")]
    [SerializeField] MeshRenderer markRenderer;

    MaterialPropertyBlock block;

    // The hit, kept twice: in world space for a surface that has since gone away, and in the surface's
    // own space for one that has not.
    Vector3 worldPoint;
    Vector3 worldNormal;
    Transform surface;
    Vector3 localPoint;
    Vector3 localNormal;

    float age;
    bool live;

    void Awake()
    {
        ResolveRenderer();
    }

    /// <summary>
    /// Lays the mark on a hit and starts its life. <paramref name="hitSurface"/> is what was hit and
    /// may be null - the mark then stays where it landed rather than following anything.
    /// </summary>
    public void Show(Vector3 point, Vector3 normal, Transform hitSurface)
    {
        ResolveRenderer();

        if (normal.sqrMagnitude < 1e-8f) normal = Vector3.up;
        normal.Normalize();

        worldPoint = point;
        worldNormal = normal;
        surface = hitSurface;

        if (surface != null)
        {
            localPoint = surface.InverseTransformPoint(point);
            localNormal = surface.InverseTransformDirection(normal);
        }

        age = 0f;
        live = true;

        Apply(0f);
        gameObject.SetActive(true);
    }

    void Update()
    {
        if (!live) return;

        age += Time.deltaTime;

        float progress = lifetime > 0f ? Mathf.Clamp01(age / lifetime) : 1f;
        Apply(progress);

        if (progress >= 1f)
        {
            live = false;
            ReturnToPool();
        }
    }

    /// <summary>
    /// Poses the mark for a point in its life. <paramref name="progress"/> 0 is the impact; by 1 the
    /// mark is black and buried, which is what lets it go without a pop.
    /// </summary>
    void Apply(float progress)
    {
        float fade = Mathf.InverseLerp(hold, 1f, progress);

        Vector3 normal = SurfaceNormal();
        transform.position = AnchorPoint() + normal * (skin - sinkDepth * fade);

        // The disc's dome points along its own up, so aligning up with the normal is the whole fit.
        transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);

        if (markRenderer == null) return;

        markRenderer.GetPropertyBlock(block);
        block.SetColor(BaseColorId, Color.Lerp(startTint, endTint, fade));
        markRenderer.SetPropertyBlock(block);
    }

    Vector3 AnchorPoint()
    {
        return surface != null ? surface.TransformPoint(localPoint) : worldPoint;
    }

    Vector3 SurfaceNormal()
    {
        return surface != null ? surface.TransformDirection(localNormal).normalized : worldNormal;
    }

    void ResolveRenderer()
    {
        if (markRenderer == null) markRenderer = GetComponentInChildren<MeshRenderer>(true);
        if (block == null) block = new MaterialPropertyBlock();
    }
}
