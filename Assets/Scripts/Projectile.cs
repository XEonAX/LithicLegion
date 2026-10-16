using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A bullet that flies along its own forward (blue Z) axis and stops at the first thing it touches,
/// leaving an <see cref="ImpactMark"/> where it stopped.
///
/// Spawn it with Instantiate(prefab, barrel.position, barrel.rotation) - it starts flying the moment
/// it is enabled and is never parented to the barrel, so the barrel can carry on moving without
/// dragging the bullet with it.
///
/// The swept volume is the CapsuleCollider on this object, so a long bullet cannot tunnel through
/// thin geometry and it hits exactly what its collider would have touched. No Rigidbody is involved;
/// the transform is driven directly.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CapsuleCollider))]
public class Projectile : MonoBehaviour
{
    /// <summary>What the bullet ran into, in a shape the Inspector can serialize.</summary>
    [Serializable]
    public struct BulletHit
    {
        [Tooltip("World-space point of contact.")]
        public Vector3 point;
        [Tooltip("World-space surface normal at the point of contact.")]
        public Vector3 normal;
        [Tooltip("Distance travelled from the muzzle.")]
        public float distance;
        [Tooltip("The collider that was hit.")]
        public Collider collider;
    }

    // Keeps the bullet from sinking into the surface it stopped against.
    const float StopShort = 0.001f;

    [Header("Flight")]
    [Tooltip("Units per second along the bullet's own forward axis.")]
    [SerializeField] float speed = 60f;
    [Tooltip("Give up after travelling this far without hitting anything.")]
    [SerializeField] float maxDistance = 300f;

    [Header("Collision")]
    [Tooltip("Layers this bullet can hit. Leave the shooter's own layer out of this.")]
    [SerializeField] LayerMask hitMask = ~0;
    [SerializeField] QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Impact")]
    [Tooltip("Destroy the bullet where it hit. Untick to leave it parked at the impact point.")]
    [SerializeField] bool destroyOnHit = true;
    [Tooltip("Mark left at the impact point. Leave it empty for bullets that hit without marking.")]
    [SerializeField] ImpactMark impactMarkPrefab;
    [Tooltip("Push delivered to whatever the bullet stops against, in newton-seconds, along the " +
             "direction of travel. Applied at the contact point, so an off-centre hit spins the target " +
             "as well as shoving it. Only Rigidbodies that are not kinematic can take it - set to 0 to " +
             "shoot without pushing anything.")]
    [SerializeField] float impactImpulse = 4f;
    [Tooltip("Raised once, where the bullet hit something.")]
    [SerializeField] UnityEvent<BulletHit> onHit = new UnityEvent<BulletHit>();

    readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    CapsuleCollider capsule;
    Vector3 direction;
    float travelled;
    bool spent;

    /// <summary>True once the bullet has hit something; <see cref="HitInfo"/> is then valid.</summary>
    public bool HasHit { get; private set; }

    /// <summary>Raw physics result of the impact.</summary>
    public RaycastHit HitInfo { get; private set; }

    /// <summary>Code-facing twin of <see cref="onHit"/>.</summary>
    public event Action<BulletHit> Hit;

    void Awake()
    {
        capsule = GetComponent<CapsuleCollider>();
    }

    void OnEnable()
    {
        // Capture the pose the bullet was spawned with. It is unparented, so this is the only moment
        // the barrel's direction matters.
        direction = transform.forward;
        travelled = 0f;
        HasHit = false;
        spent = false;
    }

    void Update()
    {
        if (spent || capsule == null) return;

        float step = Mathf.Min(speed * Time.deltaTime, maxDistance - travelled);
        if (step <= 0f)
        {
            Retire();
            return;
        }

        GetWorldCapsule(capsule, out Vector3 pointA, out Vector3 pointB, out float radius);

        if (TryFindHit(pointA, pointB, radius, step, out RaycastHit hit))
        {
            transform.position += direction * Mathf.Max(0f, hit.distance - StopShort);
            travelled += hit.distance;

            HitInfo = hit;
            HasHit = true;
            spent = true;

            var impact = new BulletHit
            {
                point = hit.point,
                normal = hit.normal,
                distance = travelled,
                collider = hit.collider,
            };
            ApplyImpactForce(impact);
            SpawnImpactMark(impact);

            onHit?.Invoke(impact);
            Hit?.Invoke(impact);

            if (destroyOnHit) Destroy(gameObject);
            return;
        }

        transform.position += direction * step;
        travelled += step;
        if (travelled >= maxDistance) Retire();
    }

    void Retire()
    {
        spent = true;
        if (destroyOnHit) Destroy(gameObject);
    }

    /// <summary>
    /// Shoves whatever the bullet stopped against. The impulse goes in at the contact point rather than
    /// through the centre of mass, so a hit near an edge makes the target tumble instead of sliding
    /// flat - which is what sells the hit. Scenery and kinematic bodies have nothing to push and are
    /// skipped, and a mass of 1 is assumed to be the ordinary case: a heavier target simply moves less.
    /// </summary>
    void ApplyImpactForce(BulletHit impact)
    {
        if (impactImpulse <= 0f || impact.collider == null) return;

        Rigidbody body = impact.collider.attachedRigidbody;
        if (body == null || body.isKinematic) return;

        body.AddForceAtPosition(direction * impactImpulse, impact.point, ForceMode.Impulse);
    }

    /// <summary>
    /// Leaves a mark lying on whatever the bullet stopped against. The mark remembers the hit in that
    /// object's own space, so shooting something that moves does not leave the mark hanging in the air
    /// where the surface used to be.
    /// </summary>
    void SpawnImpactMark(BulletHit impact)
    {
        if (impactMarkPrefab == null) return;

        Transform surface = impact.collider != null ? impact.collider.transform : null;

        ImpactMark mark = ObjectPool.Rent(impactMarkPrefab, impact.point, Quaternion.identity);
        mark.Show(impact.point, impact.normal, surface);
    }

    /// <summary>Sweeps the capsule forward and returns the nearest real impact.</summary>
    bool TryFindHit(Vector3 pointA, Vector3 pointB, float radius, float distance, out RaycastHit hit)
    {
        int count = Physics.CapsuleCastNonAlloc(pointA, pointB, radius, direction, hitBuffer,
                                                distance, hitMask, triggerInteraction);

        hit = default;
        float nearest = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = hitBuffer[i];
            if (candidate.collider == null) continue;
            if (IsSelf(candidate.collider.transform)) continue;

            // Zero distance with no normal means the sweep started inside something, such as the
            // muzzle poking into its own barrel. That is not an impact.
            if (candidate.distance <= 0f && candidate.normal == Vector3.zero) continue;
            if (candidate.distance >= nearest) continue;

            nearest = candidate.distance;
            hit = candidate;
            found = true;
        }

        return found;
    }

    bool IsSelf(Transform candidate)
    {
        return candidate == transform || candidate.IsChildOf(transform);
    }

    /// <summary>Expands the collider's capsule into the world-space segment and radius a sweep needs.</summary>
    static void GetWorldCapsule(CapsuleCollider collider, out Vector3 pointA, out Vector3 pointB, out float worldRadius)
    {
        Transform shape = collider.transform;
        Vector3 scale = shape.lossyScale;

        float axisScale, radiusScaleA, radiusScaleB;
        switch (collider.direction)
        {
            case 0: // X axis
                axisScale = scale.x; radiusScaleA = scale.y; radiusScaleB = scale.z;
                break;
            case 2: // Z axis
                axisScale = scale.z; radiusScaleA = scale.x; radiusScaleB = scale.y;
                break;
            default: // Y axis
                axisScale = scale.y; radiusScaleA = scale.x; radiusScaleB = scale.z;
                break;
        }

        worldRadius = collider.radius * Mathf.Max(Mathf.Abs(radiusScaleA), Mathf.Abs(radiusScaleB));

        // Unity's height spans the caps too, so the sphere centres sit (height / 2 - radius) apart.
        float worldHeight = Mathf.Max(collider.height * Mathf.Abs(axisScale), worldRadius * 2f);
        float halfSegment = Mathf.Max(0f, worldHeight * 0.5f - worldRadius);

        Vector3 axis = shape.TransformDirection(AxisVector(collider.direction)).normalized;
        Vector3 center = shape.TransformPoint(collider.center);

        pointA = center + axis * halfSegment;
        pointB = center - axis * halfSegment;
    }

    static Vector3 AxisVector(int direction)
    {
        switch (direction)
        {
            case 0: return Vector3.right;
            case 2: return Vector3.forward;
            default: return Vector3.up;
        }
    }

    void OnDrawGizmosSelected()
    {
        Vector3 from = transform.position;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(from, from + transform.forward * 2f);
    }
}
