using UnityEngine;

/// <summary>
/// A laser beam drawn between two world points: a white-hot core wrapped in a coloured glow.
///
/// Two <see cref="LineRenderer"/>s share one additive material and differ only in width and intensity,
/// so the bright centre and the halo always line up. The component owns nothing that moves - the gun
/// pushes both ends in every frame - which lets the beam follow a live aim point without being parented
/// to the barrel and inheriting its scale.
///
/// Created at runtime by the held emitters through <see cref="Create"/>; there is no prefab to wire.
/// </summary>
[DisallowMultipleComponent]
public sealed class BeamLine : MonoBehaviour
{
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    static readonly int FadeId = Shader.PropertyToID("_Fade");

    [Tooltip("Additive beam material. Its shader is LithicLegion/Beam.")]
    [SerializeField] Material material;
    [Tooltip("Width of the white core, in world units.")]
    [SerializeField] float coreWidth = 0.035f;
    [Tooltip("Width of the coloured halo, in world units.")]
    [SerializeField] float glowWidth = 0.24f;
    [Tooltip("HDR brightness of the core.")]
    [SerializeField] float coreIntensity = 9f;
    [Tooltip("HDR brightness of the halo.")]
    [SerializeField] float glowIntensity = 1.6f;

    LineRenderer core;
    LineRenderer glow;
    MaterialPropertyBlock block;

    Vector3 start;
    Vector3 end;
    bool hasEnds;
    float fade;

    /// <summary>True while the beam is being drawn at all.</summary>
    public bool IsVisible => fade > 0f && hasEnds;

    /// <summary>
    /// Builds a beam under <paramref name="parent"/>. The line is rendered in world space, so the
    /// parent only decides what the beam is cleaned up with - the gun rig can move freely underneath.
    /// </summary>
    public static BeamLine Create(string name, Transform parent, Material beamMaterial)
    {
        var holder = new GameObject(name);
        // Built while inactive so Awake sees the material the caller is about to assign.
        holder.SetActive(false);
        holder.transform.SetParent(parent, false);

        var beam = holder.AddComponent<BeamLine>();
        beam.material = beamMaterial;
        holder.SetActive(true);
        return beam;
    }

    void Awake()
    {
        EnsureBuilt();
        Apply();
    }

    void OnDestroy()
    {
        // The renderers are children of this object, so they go with it.
    }

    /// <summary>Puts the beam between two world points and lights it fully.</summary>
    public void Show(Vector3 from, Vector3 to)
    {
        EnsureBuilt();
        start = from;
        end = to;
        hasEnds = true;
        fade = 1f;
        Apply();
    }

    /// <summary>0 hides the beam; 1 is full strength. The beam keeps its last endpoints while fading.</summary>
    public void SetFade(float value)
    {
        fade = Mathf.Clamp01(value);
        Apply();
    }

    public void SetColor(Color value)
    {
        EnsureBuilt();
        block.SetColor(ColorId, value);
        core.SetPropertyBlock(block);
        glow.SetPropertyBlock(block);
    }

    public void SetWidths(float coreValue, float glowValue)
    {
        EnsureBuilt();
        coreWidth = Mathf.Max(0.001f, coreValue);
        glowWidth = Mathf.Max(coreWidth, glowValue);
        ApplyWidths();
    }

    public void SetIntensity(float coreValue, float glowValue)
    {
        EnsureBuilt();
        coreIntensity = Mathf.Max(0f, coreValue);
        glowIntensity = Mathf.Max(0f, glowValue);
        ApplyIntensity();
    }

    /// <summary>Stops drawing immediately, without a fade.</summary>
    public void Hide()
    {
        fade = 0f;
        Apply();
    }

    void EnsureBuilt()
    {
        block ??= new MaterialPropertyBlock();
        if (core != null) return;

        if (material == null) material = Shader.Find("LithicLegion/Beam") != null
            ? new Material(Shader.Find("LithicLegion/Beam")) : null;

        core = MakeLine("Core", coreWidth, coreIntensity);
        glow = MakeLine("Glow", glowWidth, glowIntensity);
    }

    LineRenderer MakeLine(string name, float width, float intensity)
    {
        var child = new GameObject(name);
        child.transform.SetParent(transform, false);

        var line = child.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.numCapVertices = 2;
        line.numCornerVertices = 0;
        line.generateLightingData = false;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.positionCount = 2;
        line.widthMultiplier = width;
        line.widthCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.85f, 0.9f),
            new Keyframe(1f, 0.25f));

        var properties = new MaterialPropertyBlock();
        properties.SetFloat(IntensityId, intensity);
        line.SetPropertyBlock(properties);

        return line;
    }

    void Apply()
    {
        if (core == null) return;

        bool visible = fade > 0f && hasEnds;
        core.enabled = visible;
        glow.enabled = visible;

        if (!visible) return;

        core.SetPosition(0, start);
        core.SetPosition(1, end);
        glow.SetPosition(0, start);
        glow.SetPosition(1, end);

        ApplyWidths();
        ApplyIntensity();
    }

    void ApplyWidths()
    {
        if (core == null) return;
        core.widthMultiplier = coreWidth;
        glow.widthMultiplier = glowWidth;
    }

    void ApplyIntensity()
    {
        if (core == null) return;

        SetValues(core, coreIntensity);
        SetValues(glow, glowIntensity);
    }

    void SetValues(LineRenderer line, float intensity)
    {
        block ??= new MaterialPropertyBlock();
        line.GetPropertyBlock(block);
        block.SetFloat(IntensityId, intensity);
        block.SetFloat(FadeId, fade);
        line.SetPropertyBlock(block);
    }
}
