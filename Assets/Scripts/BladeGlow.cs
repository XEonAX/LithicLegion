using UnityEngine;

/// <summary>
/// Recolours one blade's shader graph edges without making a material per gun - and brightens them
/// while a charge builds.
///
/// Every mode reuses the same blade mesh and the same shared material, so a per-renderer
/// <see cref="MaterialPropertyBlock"/> is what gives the portal pair its orange and blue and the rail
/// gun its teal. The base colours are authored here; <see cref="SetCharge"/> multiplies them and
/// <see cref="Pulse"/> adds a short flash after a shot.
/// </summary>
[DisallowMultipleComponent]
public sealed class BladeGlow : MonoBehaviour
{
    static readonly int RimColorId = Shader.PropertyToID("_RimColor");
    static readonly int RimIntensityId = Shader.PropertyToID("_RimIntensity");
    static readonly int IntersectionColorId = Shader.PropertyToID("_IntersectionColor");
    static readonly int IntersectionIntensityId = Shader.PropertyToID("_IntersectionIntensity");

    [Header("Colour")]
    [Tooltip("Colour of the blade's rim light. HDR: above 1 blooms.")]
    [SerializeField] Color rimColor = new Color(0.3f, 0.75f, 3f, 1f);
    [Tooltip("Base strength of the rim light.")]
    [SerializeField] float rimIntensity = 5f;
    [Tooltip("Colour where the blade meets the world.")]
    [SerializeField] Color intersectionColor = new Color(0.15f, 0.55f, 3f, 1f);
    [Tooltip("Base strength of the intersection light.")]
    [SerializeField] float intersectionIntensity = 4f;

    [Header("Pulse")]
    [Tooltip("How much brighter a shot makes the blade. Charging deliberately does not change it.")]
    [SerializeField] float pulseBoost = 2f;

    MeshRenderer blade;
    MaterialPropertyBlock block;

    float pulse;
    float pulseDuration;
    float pulseTime = -1f;

    void Awake()
    {
        blade = GetComponent<MeshRenderer>();
        block = new MaterialPropertyBlock();
        Apply();
    }

    void OnEnable()
    {
        if (blade == null) blade = GetComponent<MeshRenderer>();
        if (block == null) block = new MaterialPropertyBlock();
        Apply();
    }

    /// <summary>A short extra flash after a shot. <paramref name="duration"/> is in seconds.</summary>
    public void Pulse(float strength, float duration)
    {
        pulse = Mathf.Max(0f, strength);
        pulseDuration = Mathf.Max(0.01f, duration);
        pulseTime = 0f;
        Apply();
    }

    void Update()
    {
        if (pulseTime < 0f) return;

        pulseTime += Time.deltaTime;
        if (pulseTime >= pulseDuration)
        {
            pulseTime = -1f;
            pulse = 0f;
            Apply();
            return;
        }

        Apply();
    }

    void Apply()
    {
        if (blade == null) return;

        block ??= new MaterialPropertyBlock();

        float multiplier = 1f + (pulse > 0f
            ? pulse * (1f - Mathf.Clamp01(pulseTime / pulseDuration)) * pulseBoost
            : 0f);

        blade.GetPropertyBlock(block);
        block.SetColor(RimColorId, rimColor * multiplier);
        block.SetFloat(RimIntensityId, rimIntensity * multiplier);
        block.SetColor(IntersectionColorId, intersectionColor * multiplier);
        block.SetFloat(IntersectionIntensityId, intersectionIntensity * multiplier);
        blade.SetPropertyBlock(block);
    }

    /// <summary>Lets a builder script tint a cloned blade without exposing the fields in play code.</summary>
    public void SetColours(Color rim, Color intersection, float rimPower, float intersectionPower)
    {
        rimColor = rim;
        intersectionColor = intersection;
        rimIntensity = rimPower;
        intersectionIntensity = intersectionPower;
        Apply();
    }
}
