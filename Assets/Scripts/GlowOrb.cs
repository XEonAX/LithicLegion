using UnityEngine;

/// <summary>
/// A soft additive glow billboard with a point light behind it: the bright ball that sits at a portal
/// gun's muzzle, the growing orb on a charging rail gun, and the flare where either beam lands.
///
/// The quad is built at runtime and always faces the camera, so it reads as a round source from every
/// angle. Its brightness and size are pushed in by the emitter, and the light is what actually lights
/// the floor around it - the extra kick the reference shots get from their barrel light.
/// </summary>
[DisallowMultipleComponent]
public sealed class GlowOrb : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [Tooltip("Additive radial material. The muzzle flash material works and is tinted per instance.")]
    [SerializeField] Material material;
    [Tooltip("Steady colour. HDR values above 1 are what the bloom picks up.")]
    [SerializeField] Color color = Color.white;
    [Tooltip("World size of the glow at full strength.")]
    [SerializeField] float size = 0.6f;
    [Tooltip("HDR brightness multiplier at full strength.")]
    [SerializeField] float brightness = 5f;
    [Tooltip("Range of the point light.")]
    [SerializeField] float lightRange = 6f;
    [Tooltip("Intensity of the point light at full strength.")]
    [SerializeField] float lightIntensity = 8f;
    [Tooltip("Shadow mode for the point light. The rail gun's flash casts a hard shadow; the portal glows do not.")]
    [SerializeField] LightShadows lightShadows = LightShadows.None;
    [Tooltip("How dark the cast shadow gets. 1 is fully black.")]
    [Range(0f, 1f)]
    [SerializeField] float shadowStrength = 0.9f;

    MeshRenderer quad;
    Light glowLight;
    MaterialPropertyBlock block;
    Camera cam;

    float amount;
    float flashTime = -1f;
    float flashDuration = 0.3f;
    float flashSize = 1f;
    float flashBrightness = 8f;
    Color flashColor = Color.white;

    public static GlowOrb Create(string name, Transform parent, Material glowMaterial)
    {
        var holder = new GameObject(name);
        // Built while inactive so Awake sees the material the caller is about to assign.
        holder.SetActive(false);
        holder.transform.SetParent(parent, false);

        var orb = holder.AddComponent<GlowOrb>();
        orb.material = glowMaterial;
        holder.SetActive(true);
        return orb;
    }

    void Awake()
    {
        Build();
        Hide();
    }

    /// <summary>Colour and size for this particular emitter. The rail gun's flare is much bigger.</summary>
    public void Configure(Color tint, float worldSize, float hdrBrightness, float range, float lightPower,
                          LightShadows shadows = LightShadows.None)
    {
        color = tint;
        size = worldSize;
        brightness = hdrBrightness;
        lightRange = range;
        lightIntensity = lightPower;
        lightShadows = shadows;

        Build();
        if (glowLight != null) ApplyLightShadows();

        if (amount > 0f) ApplySteady(amount);
    }

    /// <summary>Continuous level for a held beam or a charging rail gun, 0 off to 1 full.</summary>
    public void SetAmount(float value)
    {
        amount = Mathf.Clamp01(value);
        flashTime = -1f;

        Build();
        if (amount <= 0.001f)
        {
            Hide();
            return;
        }

        ApplySteady(amount);
    }

    /// <summary>
    /// A one-shot flare at a world point. Used where a beam lands and for the rail gun's shot, where
    /// size and brightness are far past the steady values.
    /// </summary>
    public void Flash(Vector3 position, Color tint, float worldSize, float hdrBrightness, float duration)
    {
        Build();

        transform.position = position;
        FaceCamera();
        flashColor = tint;
        flashSize = Mathf.Max(0.01f, worldSize);
        flashBrightness = Mathf.Max(0f, hdrBrightness);
        flashDuration = Mathf.Max(0.01f, duration);
        flashTime = 0f;

        ApplyFlash(0f);
    }

    public void Hide()
    {
        amount = 0f;
        flashTime = -1f;

        if (quad != null) quad.enabled = false;
        if (glowLight != null) glowLight.enabled = false;
        transform.localScale = Vector3.one * size;
    }

    void Build()
    {
        block ??= new MaterialPropertyBlock();
        if (quad != null) return;

        var filter = gameObject.GetComponent<MeshFilter>();
        if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = QuadMesh();

        quad = gameObject.GetComponent<MeshRenderer>();
        if (quad == null) quad = gameObject.AddComponent<MeshRenderer>();
        quad.sharedMaterial = material;
        quad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        quad.receiveShadows = false;

        var lightObject = new GameObject("Light");
        lightObject.transform.SetParent(transform, false);
        glowLight = lightObject.AddComponent<Light>();
        glowLight.type = LightType.Point;
        glowLight.shadows = LightShadows.None;
        glowLight.renderMode = LightRenderMode.Auto;
        glowLight.range = lightRange;
        glowLight.intensity = 0f;
        glowLight.color = color;
        ApplyLightShadows();

        cam = Camera.main;
    }

    void FaceCamera()
    {
        if (cam == null) cam = Camera.main;
        if (cam != null) transform.rotation = cam.transform.rotation;
    }

    void ApplyLightShadows()
    {
        if (glowLight == null) return;

        glowLight.shadows = lightShadows;
        glowLight.shadowStrength = shadowStrength;
        glowLight.shadowBias = 0.02f;
        glowLight.shadowNormalBias = 0.2f;
        glowLight.shadowNearPlane = 0.1f;
    }

    void Update()
    {
        FaceCamera();

        if (flashTime >= 0f)
        {
            flashTime += Time.deltaTime;
            float progress = flashTime / flashDuration;

            if (progress >= 1f) Hide();
            else ApplyFlash(progress);
            return;
        }

        if (amount > 0.001f) ApplySteady(amount);
    }

    void ApplySteady(float value)
    {
        float scale = size * Mathf.Lerp(0.25f, 1f, value);
        float fade = Mathf.Lerp(0.35f, 1f, value);

        transform.localScale = Vector3.one * scale;
        FaceCamera();
        Paint(color, brightness * fade, lightIntensity * value);
        quad.enabled = true;
        glowLight.enabled = true;
    }

    void ApplyFlash(float progress)
    {
        float fade = Mathf.Pow(1f - progress, 2f);
        float grow = 1f + 0.35f * progress;

        transform.localScale = Vector3.one * (flashSize * grow);
        Paint(flashColor, flashBrightness * fade, lightIntensity * 4f * fade);
        quad.enabled = true;
        glowLight.enabled = true;
    }

    void Paint(Color tint, float hdrBrightness, float lightPower)
    {
        block ??= new MaterialPropertyBlock();
        quad.GetPropertyBlock(block);
        block.SetColor(BaseColorId, tint * hdrBrightness);
        quad.SetPropertyBlock(block);

        glowLight.color = tint;
        glowLight.intensity = Mathf.Max(0f, lightPower);
        glowLight.range = lightRange;
    }

    static Mesh sharedQuad;

    /// <summary>A unit quad in the XY plane, built once and shared by every glow.</summary>
    static Mesh QuadMesh()
    {
        if (sharedQuad != null) return sharedQuad;

        sharedQuad = new Mesh { name = "Glow Quad" };
        sharedQuad.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
        };
        sharedQuad.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
        };
        sharedQuad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        sharedQuad.RecalculateNormals();
        return sharedQuad;
    }
}
