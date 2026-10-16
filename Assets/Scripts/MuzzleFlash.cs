using UnityEngine;

/// <summary>
/// The flame at the muzzle: an emissive crown that snaps out at the shot and is gone a few frames
/// later. It points along its own forward (blue Z), so it wants to be a child of the barrel it fires
/// from - the barrel's pose is then the flash's pose and nothing has to move it.
///
/// One of these lives on each barrel for the whole session, switched off. Firing calls
/// <see cref="Play"/>, which snaps it back to full size and brightness; it fades itself out and
/// switches off again. Nothing is instantiated, pooled or destroyed, so a burst costs only the draw
/// calls its visible frames actually need.
///
/// The material owns the look. Whatever tint and emission it declares is what the flash fades *from*,
/// so the glow can live in either channel without this having to know which.
/// </summary>
[DisallowMultipleComponent]
public sealed class MuzzleFlash : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [Header("Timing")]
    [Tooltip("How long the flash lives, in seconds. Keep it well under the shot interval: a flash that " +
             "outlives its shot reads as a muzzle that is stuck on.")]
    [SerializeField] float lifetime = 0.07f;

    [Header("Size")]
    [Tooltip("Scale at the instant of firing, as a multiple of this object's own scale. Over 1 makes " +
             "the flash punch out past its modelled size at the moment of the shot.")]
    [SerializeField] float startScale = 1f;
    [Tooltip("Scale at the end of the fade. It shrinks into the muzzle as it dies.")]
    [SerializeField] float endScale = 0.35f;

    [Header("Colour")]
    [Tooltip("Fades the material's own tint and emission to black across the life. The material decides " +
             "what the flash looks like; this only decides how it goes out. Untick to hold the " +
             "material's colour flat and let the shrink do all the work.")]
    [SerializeField] bool fadeToBlack = true;

    [Header("Variation")]
    [Tooltip("Random roll about the bore, in degrees, so a burst does not stamp out the same flash " +
             "over and over. Kept small: a big roll swings the tall petal off vertical.")]
    [SerializeField] Vector2 roll = new Vector2(-14f, 14f);

    [Tooltip("The mesh to tint. Leave empty to use the first MeshRenderer on this object or below it.")]
    [SerializeField] MeshRenderer flashRenderer;

    MaterialPropertyBlock block;

    // The pose the barrel put this at, kept so every shot can roll the flame a fresh way from the same
    // starting point rather than drifting further each time.
    Quaternion restRotation;
    Vector3 restScale;

    // Read off the shared material once. Hard-coding the colours here instead would quietly override
    // the material, and a flash whose glow sits in the emission would then never go out: only the
    // tint would fade while the emission stayed at full strength.
    Color restTint = Color.white;
    Color restEmission = Color.black;

    bool posed;
    bool coloursTaken;
    float age = -1f;    // negative while the flash is off

    /// <summary>True while the flash is lit.</summary>
    public bool IsPlaying => age >= 0f;

    void Awake()
    {
        ResolveRenderer();
        CaptureRestPose();
        CaptureRestColours();
    }

    /// <summary>
    /// Lights the flash from the start of its life. The muzzle pose is this object's own, so a gun
    /// only has to say when. Calling it on a live flash restarts it rather than stacking a second.
    /// </summary>
    public void Play()
    {
        // Activated first so Awake has run: an object that starts switched off in the scene has not
        // captured its rest pose yet, and capturing after the roll would bake the roll in.
        gameObject.SetActive(true);

        ResolveRenderer();
        CaptureRestPose();
        CaptureRestColours();

        age = 0f;

        // Rolled about the flash's own forward, which is the bore: the flame spins around the barrel
        // instead of being tipped off it.
        transform.localRotation = restRotation * Quaternion.Euler(0f, 0f, Random.Range(roll.x, roll.y));

        Apply(0f);
    }

    void Update()
    {
        if (age < 0f) return;

        age += Time.deltaTime;

        float progress = lifetime > 0f ? Mathf.Clamp01(age / lifetime) : 1f;
        Apply(progress);

        if (progress < 1f) return;

        // Spent. Switch off rather than sit there drawing a mesh that is tinted to nothing.
        age = -1f;
        gameObject.SetActive(false);
    }

    /// <summary>Poses the flash for a point in its life: 0 is the shot, 1 is gone.</summary>
    void Apply(float progress)
    {
        transform.localScale = restScale * Mathf.Lerp(startScale, endScale, progress);

        if (flashRenderer == null) return;

        float fade = fadeToBlack ? 1f - progress : 1f;

        // Both channels scale by the same factor. With additive blending a black tint and a black
        // emission both contribute nothing, so this ends invisible however the material is set up.
        flashRenderer.GetPropertyBlock(block);
        block.SetColor(BaseColorId, restTint * fade);
        block.SetColor(EmissionColorId, restEmission * fade);
        flashRenderer.SetPropertyBlock(block);
    }

    void CaptureRestPose()
    {
        if (posed) return;

        restRotation = transform.localRotation;
        restScale = transform.localScale;
        posed = true;
    }

    void CaptureRestColours()
    {
        if (coloursTaken || flashRenderer == null) return;

        Material source = flashRenderer.sharedMaterial;
        if (source == null) return;

        if (source.HasProperty(BaseColorId)) restTint = source.GetColor(BaseColorId);
        if (source.HasProperty(EmissionColorId)) restEmission = source.GetColor(EmissionColorId);

        coloursTaken = true;
    }

    void ResolveRenderer()
    {
        if (flashRenderer == null) flashRenderer = GetComponentInChildren<MeshRenderer>(true);
        if (block == null) block = new MaterialPropertyBlock();
    }
}
