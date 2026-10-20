using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The rail gun's shot flare: a small sphere with HDR emission and a point light behind it.
///
/// A sphere is used instead of a billboard because the reference is watched from every angle - an HDR
/// emissive ball looks the same all the way round, and the scene's existing bloom supplies the soft
/// halo. The sphere's own renderer never casts shadows; the point light does, so the player sphere
/// puts a silhouette on the floor exactly like the reference.
/// </summary>
[DisallowMultipleComponent]
public sealed class GlowSphere : MonoBehaviour
{
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    static readonly int SoftnessId = Shader.PropertyToID("_Softness");

    [Tooltip("Material using LithicLegion/GlowSphere.")]
    [SerializeField] Material material;
    [Tooltip("How far the flare's light reaches.")]
    [SerializeField] float lightRange = 10f;
    [Tooltip("Light intensity at the shot.")]
    [SerializeField] float lightPower = 22f;
    [Tooltip("The flare's light is what casts the player's shadow.")]
    [SerializeField] LightShadows lightShadows = LightShadows.Hard;
    [Tooltip("Softness of the sphere's edge. Higher keeps more of the centre hot and the rim softer.")]
    [SerializeField] float edgeSoftness = 3.5f;

    MeshRenderer sphere;
    Light flareLight;
    Material sphereMaterial;

    float age = -1f;
    float duration;
    float diameter;
    float brightness;
    Color tint = Color.white;

    /// <summary>True while the flare is lit.</summary>
    public bool IsLit => age >= 0f;

    public static GlowSphere Create(string name, Transform parent, Material sphereMaterial)
    {
        var holder = new GameObject(name);
        holder.SetActive(false);
        holder.transform.SetParent(parent, false);

        var glow = holder.AddComponent<GlowSphere>();
        glow.material = sphereMaterial;
        holder.SetActive(true);
        return glow;
    }

    void Awake()
    {
        Build();
        Hide();
    }

    /// <summary>Colour and light strength for this weapon.</summary>
    public void Configure(Color colour, float range, float power, LightShadows shadows)
    {
        tint = colour;
        lightRange = range;
        lightPower = power;
        lightShadows = shadows;

        Build();
        ApplyLight();
    }

    /// <summary>
    /// Lights the sphere at <paramref name="position"/> for a short flash.
    /// <paramref name="worldDiameter"/> is the sphere's diameter, not its radius.
    /// </summary>
    public void Flash(Vector3 position, Color colour, float worldDiameter, float hdrIntensity, float seconds)
    {
        Build();

        transform.position = position;
        tint = colour;
        diameter = Mathf.Max(0.01f, worldDiameter);
        brightness = Mathf.Max(0f, hdrIntensity);
        duration = Mathf.Max(0.01f, seconds);
        age = 0f;

        Apply(0f);
    }

    public void Hide()
    {
        age = -1f;
        if (sphere != null) sphere.enabled = false;
        if (flareLight != null) flareLight.enabled = false;
    }

    void Build()
    {
        if (sphere != null) return;

        var filter = gameObject.GetComponent<MeshFilter>();
        if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = SphereMesh();

        sphere = gameObject.GetComponent<MeshRenderer>();
        if (sphere == null) sphere = gameObject.AddComponent<MeshRenderer>();
        // A material instance is used rather than a MaterialPropertyBlock: the shader keeps its
        // parameters in UnityPerMaterial, and on this renderer a property block did not reach it.
        if (material != null) sphereMaterial = new Material(material) { name = material.name + " (Instance)" };
        sphere.sharedMaterial = sphereMaterial != null ? sphereMaterial : material;
        sphere.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sphere.receiveShadows = false;

        var lightObject = new GameObject("Light");
        lightObject.transform.SetParent(transform, false);
        flareLight = lightObject.AddComponent<Light>();
        flareLight.type = LightType.Point;
        flareLight.shadows = lightShadows;
        flareLight.shadowStrength = 0.9f;
        flareLight.shadowBias = 0.02f;
        flareLight.shadowNormalBias = 0.2f;
        flareLight.shadowNearPlane = 0.1f;
        flareLight.range = lightRange;
        flareLight.intensity = 0f;
        flareLight.color = tint;
    }

    void Update()
    {
        if (age < 0f) return;

        age += Time.deltaTime;
        float progress = age / duration;

        if (progress >= 1f) Hide();
        else Apply(progress);
    }

    void Apply(float progress)
    {
        if (sphere == null) return;

        float fade = Mathf.Pow(1f - Mathf.Clamp01(progress), 2f);
        float grow = 1f + 0.35f * Mathf.Clamp01(progress);

        transform.localScale = Vector3.one * diameter * grow;

        if (sphereMaterial == null)
        {
            sphereMaterial = new Material(sphere.sharedMaterial) { name = "GlowSphere (Instance)" };
            sphere.sharedMaterial = sphereMaterial;
        }

        sphereMaterial.SetColor(ColorId, tint);
        sphereMaterial.SetFloat(IntensityId, brightness * fade);
        sphereMaterial.SetFloat(SoftnessId, edgeSoftness);
        sphere.enabled = true;

        if (flareLight == null) return;

        flareLight.enabled = true;
        flareLight.color = NormalizedTint(tint);
        flareLight.range = lightRange;
        flareLight.intensity = lightPower * fade;
    }

    void ApplyLight()
    {
        if (flareLight == null) return;

        flareLight.range = lightRange;
        flareLight.shadows = lightShadows;
    }

    /// <summary>The light's colour must be in 0-1; its brightness lives in the intensity instead.</summary>
    static Color NormalizedTint(Color colour)
    {
        float max = Mathf.Max(colour.r, Mathf.Max(colour.g, colour.b));
        if (max <= 0.0001f) return Color.white;

        Color result = colour / max;
        result.a = 1f;
        return result;
    }

    static Mesh sharedSphere;

    /// <summary>
    /// A unit-diameter UV sphere built in code. Borrowing Unity's primitive would leave a temporary
    /// GameObject in the scene for a frame, and that object would cast its own shadow and show up in
    /// captures; a generated mesh has no side effects.
    /// </summary>
    static Mesh SphereMesh()
    {
        if (sharedSphere != null) return sharedSphere;

        const int segments = 24;
        const int rings = 16;
        int stride = segments + 1;

        var vertices = new List<Vector3>(stride * (rings + 1));
        var normals = new List<Vector3>(stride * (rings + 1));
        var uvs = new List<Vector2>(stride * (rings + 1));
        var triangles = new List<int>(segments * rings * 6);

        for (int ring = 0; ring <= rings; ring++)
        {
            float v = (float)ring / rings;
            float phi = v * Mathf.PI;
            float sinPhi = Mathf.Sin(phi);

            for (int segment = 0; segment <= segments; segment++)
            {
                float u = (float)segment / segments;
                float theta = u * Mathf.PI * 2f;

                var normal = new Vector3(sinPhi * Mathf.Cos(theta), Mathf.Cos(phi),
                                         sinPhi * Mathf.Sin(theta));

                vertices.Add(normal * 0.5f);
                normals.Add(normal);
                uvs.Add(new Vector2(u, 1f - v));
            }
        }

        for (int ring = 0; ring < rings; ring++)
        {
            for (int segment = 0; segment < segments; segment++)
            {
                int a = ring * stride + segment;
                int b = a + stride;

                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(a + 1);

                triangles.Add(a + 1);
                triangles.Add(b);
                triangles.Add(b + 1);
            }
        }

        sharedSphere = new Mesh { name = "Glow Sphere" };
        sharedSphere.SetVertices(vertices);
        sharedSphere.SetNormals(normals);
        sharedSphere.SetUVs(0, uvs);
        sharedSphere.SetTriangles(triangles, 0);
        sharedSphere.RecalculateBounds();
        return sharedSphere;
    }

}
