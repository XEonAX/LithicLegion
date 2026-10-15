using UnityEngine;

/// <summary>
/// Reticle that shrinks as the aim point gets further away: full size up close, half size at range.
///
/// Leave it on a RectTransform under the Canvas. Player pushes the hit distance in every frame with
/// <see cref="SetDistance"/>, so the crosshair never has to cast anything itself.
/// </summary>
public class CrossHair : MonoBehaviour
{
    [Header("Distance to size")]
    [Tooltip("Distance at which the reticle is still at full size.")]
    [SerializeField] float nearDistance = 5f;
    [Tooltip("Distance at which the reticle has shrunk all the way to its far size.")]
    [SerializeField] float farDistance = 40f;
    [Tooltip("Size multiplier when the target is nearer than Near Distance. 1 = the size you authored.")]
    [SerializeField] float nearScale = 1f;
    [Tooltip("Size multiplier when the target is at or beyond Far Distance.")]
    [SerializeField] float farScale = 0.5f;

    [Header("Feel")]
    [Tooltip("How fast the reticle eases towards its new size, in size-multiplier units per second. " +
             "0 changes size instantly.")]
    [SerializeField] float smoothSpeed = 4f;

    RectTransform rect;
    Vector3 authoredScale;      // the reticle's own scale, which Near Scale of 1 means "keep"
    float scaleMultiplier = 1f; // where the reticle is now
    float targetMultiplier = 1f;

    void Awake()
    {
        rect = GetComponent<RectTransform>();
        if (rect == null)
        {
            Debug.LogError($"{name}: CrossHair has to sit on a RectTransform.", this);
            enabled = false;
            return;
        }

        authoredScale = rect.localScale;
        targetMultiplier = scaleMultiplier = nearScale;
    }

    /// <summary>
    /// How far away the aim point is. Player passes the raycast hit distance here, or infinity when
    /// the shot hits nothing - distances past Far Distance all clamp to the far size.
    /// </summary>
    public void SetDistance(float distance)
    {
        float t = Mathf.InverseLerp(nearDistance, farDistance, distance);
        targetMultiplier = Mathf.Lerp(nearScale, farScale, t);
    }

    // LateUpdate so the reticle settles after Player has reported this frame's distance.
    void LateUpdate()
    {
        scaleMultiplier = smoothSpeed > 0f
            ? Mathf.MoveTowards(scaleMultiplier, targetMultiplier, smoothSpeed * Time.deltaTime)
            : targetMultiplier;

        rect.localScale = authoredScale * scaleMultiplier;
    }
}
