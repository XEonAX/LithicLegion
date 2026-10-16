using UnityEngine;

/// <summary>
/// The afterimage a dash leaves behind: the ball's own shape, flattening across the dash and stretching
/// along it at the same time, until it is a long thin streak down the dash direction that glows where
/// it stopped and then fades away.
///
/// <see cref="MagicDash"/> builds one per dash and hands it the shape to flatten from. The ghost lies
/// along its own blue Z, which the dash points down the dash, so X and Y are the axes that collapse
/// and Z is the one that reaches after the ball.
///
/// It fades by dimming the glow it was handed - the emission of a lit material, or the colour and alpha
/// of an unlit one - so an additive material adds less and less until it adds nothing at all, and the
/// ghost is gone without ever having been a black shape.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class DashGhost : MonoBehaviour
{
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    MeshRenderer ghostRenderer;
    MaterialPropertyBlock block;

    Vector3 shape = Vector3.one;    // the ball's own scale: X and Y collapse away, Z stretches out
    float meshLength = 2f;          // how long the ball's mesh is, so a world length can be asked for
    float reach = 1f;               // world length the streak stretches to
    float thinTo = 0.1f;            // how thin the streak gets, as a share of the ball's own width
    Vector3 origin;                 // the spot the ball left, which the tail of the streak stays on
    Vector3 direction = Vector3.forward;
    Color emission = Color.black;   // the material's glow at full strength
    Color baseColor = Color.white;
    AnimationCurve fade = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    float lifetime = 1f;
    float collapse = 0.12f;
    float age;
    bool playing;

    void Awake()
    {
        block = new MaterialPropertyBlock();
    }

    /// <summary>
    /// Sets the ghost going. <paramref name="ballScale"/> is the ball's own world scale, the shape it
    /// starts as. Over <paramref name="collapseSeconds"/> it flattens across the dash down to
    /// <paramref name="thinTo"/> of that width while stretching out to <paramref name="reach"/> metres
    /// long, its tail left behind on the spot the ball came from. It destroys itself once
    /// <paramref name="seconds"/> have passed.
    /// </summary>
    public void Play(Vector3 ballScale, float reach, float seconds, float collapseSeconds,
                     float thinTo, AnimationCurve fadeCurve)
    {
        shape = ballScale;
        var meshFilter = GetComponent<MeshFilter>();
        meshLength = meshFilter != null && meshFilter.sharedMesh != null
            ? Mathf.Max(0.0001f, meshFilter.sharedMesh.bounds.size.z)
            : 2f;
        this.reach = Mathf.Max(0f, reach);
        lifetime = Mathf.Max(0f, seconds);
        collapse = Mathf.Max(0f, collapseSeconds);
        this.thinTo = Mathf.Clamp01(thinTo);
        if (fadeCurve != null && fadeCurve.length > 0) fade = fadeCurve;

        origin = transform.position;
        direction = transform.forward;   // the dash aims the ghost's own Z down the dash

        ReadMaterial();
        Restretch(0f);
        ApplyGlow(1f);

        age = 0f;
        playing = true;
    }

    void Update()
    {
        if (!playing) return;

        age += Time.deltaTime;

        // Flattening and stretching run together: the ball is a streak for most of its life, not only
        // at the end of it.
        float flattening = collapse > 0f ? Mathf.Clamp01(age / collapse) : 1f;
        Restretch(Mathf.SmoothStep(0f, 1f, flattening));

        float life = lifetime > 0f ? Mathf.Clamp01(age / lifetime) : 1f;
        ApplyGlow(Mathf.Clamp01(fade.Evaluate(life)));

        if (life >= 1f) Destroy(gameObject);
    }

    /// <summary>
    /// Puts the ghost at one point along its flattening, where <paramref name="progress"/> 0 is the whole
    /// ball and 1 is the finished streak. The tail stays on the spot the ball left, so the streak grows
    /// out of it towards where the ball went rather than growing through it in both directions.
    /// </summary>
    void Restretch(float progress)
    {
        float across = Mathf.Lerp(1f, thinTo, progress);
        float length = Mathf.Lerp(shape.z, reach / meshLength, progress);

        transform.localScale = new Vector3(shape.x * across, shape.y * across, length);

        float grown = (length - shape.z) * meshLength;   // world metres added to the front of the streak
        transform.position = origin + direction * (grown * 0.5f);
    }

    /// <summary>Picks up the glow the material was authored with, so the ghost can dim it.</summary>
    void ReadMaterial()
    {
        Material material = GhostRenderer().sharedMaterial;
        if (material == null) return;

        if (material.HasProperty(EmissionColorId)) emission = material.GetColor(EmissionColorId);
        if (material.HasProperty(BaseColorId)) baseColor = material.GetColor(BaseColorId);
        else if (material.HasProperty(ColorId)) baseColor = material.GetColor(ColorId);
    }

    /// <summary>
    /// Dims the ghost towards nothing. All three colour properties are written because a material only
    /// reads the ones it has: a lit material fades through its emission, while URP's additive blend is
    /// SrcAlpha times the colour added to the background, so an unlit glow fades through the base
    /// colour's alpha. Fading either one reaches black, and black adds nothing.
    /// </summary>
    void ApplyGlow(float glow)
    {
        if (block == null) block = new MaterialPropertyBlock();

        block.SetColor(EmissionColorId, new Color(emission.r * glow, emission.g * glow, emission.b * glow, emission.a));
        block.SetColor(BaseColorId, new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * glow));
        block.SetColor(ColorId, new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * glow));
        GhostRenderer().SetPropertyBlock(block);
    }

    /// <summary>
    /// The ghost's own renderer, looked up again whenever the reference has gone. Not simply cached in
    /// Awake: a script reload in the Editor clears plain private fields without running Awake again, and
    /// a ghost caught halfway through its life would then throw on every frame it had left.
    /// </summary>
    MeshRenderer GhostRenderer()
    {
        if (ghostRenderer == null) ghostRenderer = GetComponent<MeshRenderer>();
        return ghostRenderer;
    }
}
