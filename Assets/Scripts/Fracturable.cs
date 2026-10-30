using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Breaks this object's mesh at a point of impact and throws the pieces.
///
/// The object stays static until something calls <see cref="Fracture"/>; it needs no Rigidbody of
/// its own, so a level full of these costs nothing until they are hit. Two modes:
///
/// - <see cref="FractureMode.Crater"/> carves a faceted bowl out of the surface at the impact point
///   and turns only that plug into debris. The rest of the mesh is left behind as static remnants,
///   so a Cube50 loses a crater rather than the whole block. This is the mode for large blocks.
/// - <see cref="FractureMode.Shatter"/> breaks the whole mesh. The first two cuts pass through the
///   impact point, so the shards radiate from where the shot landed. This is the mode for small
///   blocks, where a crater would leave only slivers.
///
/// In both modes the debris is thrown outward from the impact and spun, and the original object is
/// switched off. The mesh must be closed, non-self-intersecting and have Read/Write enabled in its
/// import settings - those are the constraints of the slicing algorithm in OpenFracture.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class Fracturable : MonoBehaviour
{
    public enum FractureMode
    {
        /// <summary>Remove a bowl-shaped bite at the impact point; the rest of the mesh stays put.</summary>
        Crater,
        /// <summary>Break the entire mesh apart.</summary>
        Shatter,
    }

    [Header("Fracture")]
    [Tooltip("Crater leaves the rest of a large block standing; Shatter breaks the whole mesh, " +
             "which suits a small block.")]
    [SerializeField] FractureMode mode = FractureMode.Crater;
    [Tooltip("How many debris pieces the broken section becomes.")]
    [SerializeField] int fragmentCount = 14;
    [Tooltip("Material for the newly exposed faces. Falls back to the object's own material.")]
    [SerializeField] Material insideMaterial;

    [Header("Crater")]
    [Tooltip("Radius of the crater mouth, in metres, measured across the face that was hit.")]
    [SerializeField] float craterRadius = 5f;
    [Tooltip("How deep the crater bites into the surface, in metres.")]
    [SerializeField] float craterDepth = 7f;
    [Tooltip("Number of flat faces around the crater wall. More is rounder and costs more slices.")]
    [Range(4, 16)]
    [SerializeField] int craterSides = 8;
    [Tooltip("Width of the crater floor as a fraction of its mouth, so the bowl does not end in a point.")]
    [Range(0f, 0.8f)]
    [SerializeField] float craterFloor = 0.35f;

    [Header("Blast")]
    [Tooltip("Speed in metres per second of a shard sitting on the impact point. Shards further " +
             "away are thrown proportionally slower.")]
    [SerializeField] float blastSpeed = 20f;
    [Tooltip("How strongly the blast throws shards up as well as out.")]
    [Range(0f, 1f)]
    [SerializeField] float upwards = 0.35f;
    [Tooltip("Spin given to each shard, in degrees per second.")]
    [SerializeField] float spinSpeed = 220f;
    [Tooltip("Radius the blast falls off over, as a fraction of the broken section's radius.")]
    [Range(0.1f, 2f)]
    [SerializeField] float blastRadius = 0.8f;

    [Header("Debris")]
    [Tooltip("Seconds before each shard is destroyed. 0 keeps the debris for good.")]
    [SerializeField] float debrisLifetime;

    bool broken;

    /// <summary>True once this object has been fractured. It will not fracture twice.</summary>
    public bool IsBroken => broken;

    /// <summary>
    /// Breaks the object at <paramref name="worldPoint"/>, with <paramref name="worldNormal"/>
    /// pointing out of the surface that was hit. Returns false if it was already broken.
    /// </summary>
    public bool Fracture(Vector3 worldPoint, Vector3 worldNormal)
    {
        if (broken) return false;

        MeshFilter filter = GetComponent<MeshFilter>();
        MeshRenderer sourceRenderer = GetComponent<MeshRenderer>();
        Mesh mesh = filter != null ? filter.sharedMesh : null;
        if (mesh == null || sourceRenderer == null)
        {
            Debug.LogError($"{name}: Fracturable needs a MeshFilter with a mesh and a MeshRenderer.", this);
            return false;
        }

        if (!mesh.isReadable)
        {
            Debug.LogError($"{name}: '{mesh.name}' is not readable, so it cannot be fractured. " +
                           $"Enable Read/Write on {mesh.name}'s import settings.", this);
            return false;
        }

        if (mesh.subMeshCount > 1)
        {
            Debug.LogWarning($"{name}: '{mesh.name}' has {mesh.subMeshCount} submeshes; only the " +
                             "first one is fractured.", this);
        }

        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
        Vector3 localNormal = transform.InverseTransformDirection(worldNormal).normalized;
        if (localNormal.sqrMagnitude < 0.0001f) localNormal = Vector3.up;

        // A hit reported just outside the bounds (rounded corner, a collider slightly larger than
        // the mesh) still has to cut through real geometry.
        localPoint = mesh.bounds.ClosestPoint(localPoint);

        var rests = new List<FragmentData>();
        var source = new FragmentData(mesh);

        List<FragmentData> pieces;
        if (mode == FractureMode.Crater)
        {
            FragmentData plug = Carve(source, localPoint, localNormal, rests);
            if (plug.triangleCount == 0)
            {
                Debug.LogWarning($"{name}: the crater missed the mesh; nothing was fractured.", this);
                return false;
            }

            pieces = Subdivide(new List<FragmentData> { plug }, Mathf.Max(2, fragmentCount));
        }
        else
        {
            pieces = ShatterPieces(source, localPoint, localNormal, Mathf.Max(4, fragmentCount));
        }

        pieces.RemoveAll(piece => piece == null || piece.triangleCount == 0);
        if (pieces.Count == 0)
        {
            Debug.LogWarning($"{name}: fracturing produced no usable pieces.", this);
            return false;
        }

        broken = true;
        SpawnDebris(pieces, mesh, sourceRenderer, worldPoint, worldNormal);
        if (rests.Count > 0) SpawnRemnants(rests, sourceRenderer);

        // The original is switched off rather than destroyed: it keeps the prefab instance intact
        // (and anything else hanging off it) while its mesh is replaced by the shards.
        gameObject.SetActive(false);
        return true;
    }

    // ---------------------------------------------------------------- cutting

    /// <summary>
    /// Cuts a bowl out of the mesh at the impact point. The bowl is a frustum: a mouth of
    /// <see cref="craterRadius"/> at the surface, a floor of <see cref="craterFloor"/> times that at
    /// <see cref="craterDepth"/>, and <see cref="craterSides"/> flat wall planes between them. The
    /// plug that comes out is returned; the rest of the mesh is added to <paramref name="rests"/>.
    /// </summary>
    FragmentData Carve(FragmentData source, Vector3 point, Vector3 normal, List<FragmentData> rests)
    {
        float worldScale = Mathf.Max(0.0001f,
            (Mathf.Abs(transform.lossyScale.x) + Mathf.Abs(transform.lossyScale.y) +
             Mathf.Abs(transform.lossyScale.z)) / 3f);

        float radius = Mathf.Max(0.01f, craterRadius / worldScale);
        float depth = Mathf.Max(0.01f, craterDepth / worldScale);
        float floor = radius * Mathf.Clamp(craterFloor, 0.05f, 0.9f);
        int sides = Mathf.Clamp(craterSides, 3, 32);

        Vector3 inward = -normal;
        Vector3 reference = Mathf.Abs(normal.y) > 0.95f ? Vector3.forward : Vector3.up;
        Vector3 tangent = Vector3.Cross(normal, reference).normalized;
        Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;

        // Walls lean in from the mouth to the floor. `slope` is the wall's radial travel per unit
        // of depth, so the plane through (mouth radius, surface) also passes the floor edge.
        float slope = (radius - floor) / depth;

        FragmentData plug = source;
        for (int i = 0; i < sides; i++)
        {
            float angle = i * Mathf.PI * 2f / sides;
            Vector3 outward = (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)).normalized;

            // Plane through the mouth edge, normal against the radial direction and the slope, so
            // the plug sits on its positive side.
            Vector3 planeNormal = -(outward + inward * slope).normalized;
            Vector3 origin = point + outward * radius;

            MeshSlicer.Slice(plug, planeNormal, origin, Vector2.one, Vector2.zero,
                             out FragmentData top, out FragmentData bottom);
            if (bottom.triangleCount > 0) rests.Add(bottom);
            plug = top;
            if (plug.triangleCount == 0) return plug;
        }

        // Flat crater floor, so the bowl does not taper to a point.
        MeshSlicer.Slice(plug, normal, point + inward * depth, Vector2.one, Vector2.zero,
                         out FragmentData shallow, out FragmentData deep);
        if (deep.triangleCount > 0) rests.Add(deep);
        return shallow;
    }

    /// <summary>
    /// Breaks the whole mesh. Two planes through the impact point come first, so the shards share
    /// the point where the shot landed; the quarters are then subdivided to the target count.
    /// </summary>
    List<FragmentData> ShatterPieces(FragmentData source, Vector3 point, Vector3 normal, int target)
    {
        Vector3 reference = Mathf.Abs(normal.y) > 0.95f ? Vector3.forward : Vector3.up;
        Vector3 tangent = Vector3.Cross(normal, reference).normalized;
        Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;

        var crossed = new List<FragmentData>();
        Cut(new List<FragmentData> { source }, tangent, point, crossed);
        var pieces = new List<FragmentData>();
        Cut(crossed, bitangent, point, pieces);

        return Subdivide(pieces, target);
    }

    /// <summary>Applies one plane cut to every piece in <paramref name="input"/>.</summary>
    static void Cut(List<FragmentData> input, Vector3 normal, Vector3 origin, List<FragmentData> output)
    {
        foreach (FragmentData piece in input)
        {
            MeshSlicer.Slice(piece, normal, origin, Vector2.one, Vector2.zero,
                             out FragmentData top, out FragmentData bottom);
            if (top.triangleCount > 0) output.Add(top);
            if (bottom.triangleCount > 0) output.Add(bottom);
        }
    }

    /// <summary>Subdivides pieces with random planes until there are <paramref name="target"/> of them.</summary>
    static List<FragmentData> Subdivide(List<FragmentData> pieces, int target)
    {
        var queue = new Queue<FragmentData>(pieces);
        int guard = 0;
        while (queue.Count < target && guard++ < target * 10)
        {
            FragmentData piece = queue.Dequeue();
            piece.CalculateBounds();

            if (TrySplit(piece, out FragmentData top, out FragmentData bottom))
            {
                queue.Enqueue(top);
                queue.Enqueue(bottom);
            }
            else
            {
                queue.Enqueue(piece);
            }
        }

        return new List<FragmentData>(queue);
    }

    /// <summary>
    /// Cuts a piece through its centre. A random plane through a convex piece's centre always
    /// splits it; the retries cover pieces that are concave and whose centre falls outside them.
    /// </summary>
    static bool TrySplit(FragmentData piece, out FragmentData top, out FragmentData bottom)
    {
        for (int i = 0; i < 4; i++)
        {
            Vector3 normal = Random.onUnitSphere;
            MeshSlicer.Slice(piece, normal, piece.Bounds.center, Vector2.one, Vector2.zero,
                             out top, out bottom);
            if (top.triangleCount > 0 && bottom.triangleCount > 0) return true;
        }

        top = null;
        bottom = null;
        return false;
    }

    // ---------------------------------------------------------------- objects

    /// <summary>Turns each piece into a rigidbody shard parented where the original object stood.</summary>
    void SpawnDebris(List<FragmentData> pieces, Mesh sourceMesh, MeshRenderer sourceRenderer,
                     Vector3 worldPoint, Vector3 worldNormal)
    {
        Transform root = NewRoot($"{name} Debris");

        Material surface = sourceRenderer.sharedMaterial;
        Material interior = insideMaterial != null ? insideMaterial : surface;
        if (surface == null) surface = interior;
        Collider sourceCollider = GetComponent<Collider>();

        // The blast radius follows the broken section, not the whole mesh, so a crater in a Cube50
        // throws its plug properly instead of dribbling it out over 40 m.
        float sectionRadius = mode == FractureMode.Crater
            ? craterRadius
            : sourceMesh.bounds.extents.magnitude * transform.lossyScale.magnitude;
        float radius = Mathf.Max(0.5f, sectionRadius * blastRadius);
        const float density = 20f;

        foreach (FragmentData piece in pieces)
        {
            Mesh shardMesh = piece.ToMesh();
            if (shardMesh.vertexCount == 0) continue;

            // Centre the mesh on its own origin so the shard's transform sits where the shard
            // actually is; otherwise every shard would pivot around the cube's centre.
            Vector3 offset = shardMesh.bounds.center;
            Recenter(shardMesh, offset);

            var shard = new GameObject("Shard");
            shard.layer = gameObject.layer;
            shard.transform.SetParent(root, false);
            shard.transform.localPosition = offset;
            shard.transform.localRotation = Quaternion.identity;
            shard.transform.localScale = Vector3.one;

            shard.AddComponent<MeshFilter>().sharedMesh = shardMesh;

            var renderer = shard.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { surface, interior };
            renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            renderer.receiveShadows = sourceRenderer.receiveShadows;

            var collider = shard.AddComponent<MeshCollider>();
            collider.sharedMesh = shardMesh;
            collider.convex = true;
            if (sourceCollider != null) collider.sharedMaterial = sourceCollider.sharedMaterial;

            var body = shard.AddComponent<Rigidbody>();
            Vector3 size = shardMesh.bounds.size;
            body.mass = Mathf.Clamp(size.x * size.y * size.z * density, 1f, 4000f);

            Vector3 worldCentre = root.TransformPoint(offset);
            Vector3 away = worldCentre - worldPoint;
            if (away.sqrMagnitude < 0.0001f) away = worldNormal;
            float distance = away.magnitude;

            // A crater plug sits inside the hole it came from, so pushing it along the line from
            // the impact would drive it into the crater floor and wedge it there. Throw it out of
            // the mouth instead, with a little spread across the face.
            Vector3 direction;
            if (mode == FractureMode.Crater)
            {
                Vector3 spread = Vector3.ProjectOnPlane(away, worldNormal);
                if (spread.sqrMagnitude > 0.0001f) spread = spread.normalized * 0.55f;
                direction = (worldNormal + spread + Vector3.up * upwards).normalized;
            }
            else
            {
                direction = (away / distance + worldNormal * 0.7f + Vector3.up * upwards).normalized;
            }

            float falloff = 1f - Mathf.Clamp01(distance / radius);
            float minimum = mode == FractureMode.Crater ? 0.6f : 0.45f;
            body.linearVelocity = direction * Mathf.Lerp(blastSpeed * minimum, blastSpeed, falloff);
            body.angularVelocity = Random.onUnitSphere * spinSpeed * Mathf.Deg2Rad;

            if (debrisLifetime > 0f) Destroy(shard, debrisLifetime);
        }
    }

    /// <summary>
    /// Puts the unbroken remainder of a crater back as static geometry. Mesh colliders keep the
    /// crater solid, so the player can walk into the hole and shots pass through its mouth.
    /// </summary>
    void SpawnRemnants(List<FragmentData> rests, MeshRenderer sourceRenderer)
    {
        Transform root = NewRoot($"{name} Remnants");

        Material surface = sourceRenderer.sharedMaterial;
        Material interior = insideMaterial != null ? insideMaterial : surface;
        if (surface == null) surface = interior;
        Collider sourceCollider = GetComponent<Collider>();

        foreach (FragmentData rest in rests)
        {
            Mesh mesh = rest.ToMesh();
            if (mesh.vertexCount == 0) continue;

            var remnant = new GameObject("Remnant");
            remnant.layer = gameObject.layer;
            remnant.transform.SetParent(root, false);
            remnant.transform.localPosition = Vector3.zero;
            remnant.transform.localRotation = Quaternion.identity;
            remnant.transform.localScale = Vector3.one;

            remnant.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = remnant.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { surface, interior };
            renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            renderer.receiveShadows = sourceRenderer.receiveShadows;

            var collider = remnant.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = false;
            if (sourceCollider != null) collider.sharedMaterial = sourceCollider.sharedMaterial;
        }
    }

    /// <summary>A container placed exactly where the source object stood.</summary>
    Transform NewRoot(string rootName)
    {
        var root = new GameObject(rootName);
        root.layer = gameObject.layer;
        root.transform.SetParent(transform.parent, false);
        root.transform.localPosition = transform.localPosition;
        root.transform.localRotation = transform.localRotation;
        root.transform.localScale = transform.localScale;
        return root.transform;
    }

    /// <summary>Moves a mesh's vertices so that <paramref name="offset"/> becomes its origin.</summary>
    static void Recenter(Mesh mesh, Vector3 offset)
    {
        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++) vertices[i] -= offset;
        mesh.vertices = vertices;
        mesh.RecalculateBounds();
    }
}
