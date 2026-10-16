using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Magic dash: blinks the ball a set distance along its own velocity, leaving a ghost of the ball
/// behind. The ghost is a copy of the ball's mesh that flattens on the two axes across the dash while
/// stretching along it - ending as a long thin streak down the dash direction - and glows until it has
/// faded away. A few particles are thrown off the back of it as it goes.
///
/// Sits on the rolling ball. The ghost and the puff are both built at runtime out of the ball's own
/// mesh and <see cref="effectMaterial"/>, so there is nothing to drag into the Inspector beyond the
/// material itself. A puff of your own can be dropped into <see cref="puff"/> to replace the built one.
///
/// Fired by the project-wide Sprint action, so Left Shift (or a gamepad stick press) dashes.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(MeshFilter))]
public class MagicDash : MonoBehaviour
{
    [Header("Dash")]
    [Tooltip("How far the ball blinks, in metres.")]
    [SerializeField] float distance = 3f;
    [Tooltip("The kick the dash throws the ball forward with, in newton-seconds, on top of the speed it " +
             "already had. A ball of mass 1 picks up this many metres per second, and dashes stack.")]
    [SerializeField] float impulse = 6f;
    [Tooltip("Roll speed the ball needs before a dash has a direction to go in. Slower than this the " +
             "input is ignored, because a ball at rest has no velocity to dash along.")]
    [SerializeField] float minSpeed = 0.5f;
    [Tooltip("Seconds before the ball may dash again.")]
    [SerializeField] float cooldown = 1f;
    [Tooltip("Dash along the ball's heading on the ground rather than its whole velocity, so dashing " +
             "in mid-air does not drive the ball at the floor.")]
    [SerializeField] bool keepHorizontal;
    [Tooltip("Gap left between the ball and anything the dash would otherwise have landed inside.")]
    [SerializeField] float wallPadding = 0.05f;

    [Header("Ghost")]
    [Tooltip("What the ghost and the puff are made of. Dash Glow: the same HDR blue as the bullets, " +
             "but unlit and additive, so dimming it means adding less glow rather than leaving a black " +
             "shape behind. Give it a material of the same kind, or one that emits.")]
    [SerializeField] Material effectMaterial;
    [Tooltip("Seconds from the dash until the ghost streak has gone.")]
    [SerializeField] float ghostLifetime = 1f;
    [Tooltip("Seconds the ball spends flattening into a streak. The rest of the lifetime is the streak " +
             "hanging there fading.")]
    [SerializeField] float ghostCollapse = 0.12f;
    [Tooltip("How far the streak stretches along the dash, as a multiple of the dash distance. 1 makes " +
             "it just long enough to reach back from the ball to where it came from.")]
    [SerializeField] float ghostStretch = 1f;
    [Tooltip("How thin the streak thins down to, as a share of the ball's own width. It cannot go all " +
             "the way to zero: a line with no width has nothing left to render while it fades.")]
    [Range(0f, 1f)]
    [SerializeField] float ghostThickness = 0.1f;
    [Tooltip("Glow left across the ghost's life: 1 is the material's full emission, 0 is gone.")]
    [SerializeField] AnimationCurve ghostFade = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Puff")]
    [Tooltip("Optional. Leave empty and one is built for you out of the settings below.")]
    [SerializeField] ParticleSystem puff;
    [Tooltip("Particles thrown off the back of the ghost, per dash.")]
    [SerializeField] int puffCount = 12;
    [Tooltip("Width of the cone they leave in, in degrees.")]
    [SerializeField] float puffSpread = 30f;
    [Tooltip("Size of one particle as it leaves, in metres.")]
    [SerializeField] float puffSize = 0.18f;
    [Tooltip("How fast a particle travels, in metres per second.")]
    [SerializeField] float puffSpeed = 2.5f;
    [Tooltip("Seconds a particle takes to shrink away to nothing.")]
    [SerializeField] float puffLifetime = 0.45f;

    Rigidbody rb;
    Mesh ballMesh;
    InputAction dashAction;
    bool ownsPuff;          // only tidy up a puff this built, never one that was assigned
    float nextDashTime;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        ballMesh = GetComponent<MeshFilter>().sharedMesh;

        // Sprint is the dash trigger: Left Shift on the keyboard, stick press on a pad. There is no
        // separate Dash action, so the two never fight over the same key.
        dashAction = InputSystem.actions != null ? InputSystem.actions.FindAction("Sprint") : null;
        if (dashAction == null)
        {
            Debug.LogError($"{name}: no 'Sprint' action in the project-wide input actions, so there is " +
                           "no way to trigger a dash.", this);
        }

        if (effectMaterial == null)
        {
            Debug.LogWarning($"{name}: no effect material set, so the ghost will borrow the ball's own " +
                             "and read as a solid ball rather than a glow.", this);
        }

        if (puff == null)
        {
            puff = BuildPuff();
            ownsPuff = true;
        }
    }

    void OnDestroy()
    {
        // The puff is built at the root instead of under the ball, so nothing else would clean it up.
        if (ownsPuff && puff != null) Destroy(puff.gameObject);
    }

    void Update()
    {
        if (dashAction == null || !dashAction.WasPressedThisFrame()) return;

        Dash();
    }

    /// <summary>
    /// Blinks the ball <see cref="distance"/> along its velocity, leaving the ghost on the spot it left
    /// and the puff trailing off the back of it. Does nothing while the dash is cooling down, or when
    /// the ball is too slow to have a heading. Returns whether the ball went.
    /// </summary>
    public bool Dash()
    {
        if (Time.time < nextDashTime) return false;

        Vector3 velocity = rb.linearVelocity;
        if (velocity.magnitude < minSpeed) return false;

        Vector3 heading = keepHorizontal ? Vector3.ProjectOnPlane(velocity, Vector3.up) : velocity;
        if (heading.sqrMagnitude < 1e-6f) return false;

        heading.Normalize();
        nextDashTime = Time.time + cooldown;

        Vector3 from = rb.position;
        float travelled = ClearDistance(heading);
        Vector3 to = from + heading * travelled;

        SpawnGhost(from, heading, travelled);
        EmitPuff(from, heading);

        rb.position = to;

        // The rigidbody is the truth for physics, but the transform is what everything else reads - the
        // renderer, and the constraint that carries the camera rig. Moving only the body leaves those a
        // physics step behind, which smears the ball into its new spot instead of blinking it there.
        transform.position = to;

        // The blink throws the ball forward on top of moving it: an impulse down the dash, added to the
        // speed it already had, so dashing out of a run leaves more speed on the ball than dashing out of
        // a crawl. Nothing caps it - dashing off cooldown is meant to keep building.
        rb.AddForce(heading * impulse, ForceMode.Impulse);

        return true;
    }

    /// <summary>
    /// How much of the dash the ball can actually take before it would be inside something. Sweeps the
    /// ball's own collider, so the answer is where that collider would have stopped.
    /// </summary>
    float ClearDistance(Vector3 heading)
    {
        if (rb.SweepTest(heading, out RaycastHit hit, distance, QueryTriggerInteraction.Ignore))
        {
            return Mathf.Max(0f, hit.distance - wallPadding);
        }

        return distance;
    }

    /// <summary>Drops a ghost of the ball on the spot it is leaving, pointing down the dash.</summary>
    void SpawnGhost(Vector3 position, Vector3 heading, float travelled)
    {
        if (ballMesh == null) return;

        var ghostObject = new GameObject($"{name} Ghost");

        // Aimed down the dash so the ghost's own Z is the axis that stretches. A plain FromToRotation
        // rather than a LookRotation: a ball falling straight down is no rotation's up vector, and this
        // one does not care.
        ghostObject.transform.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.forward, heading));

        ghostObject.AddComponent<MeshFilter>().sharedMesh = ballMesh;

        var ballRenderer = GetComponent<MeshRenderer>();
        var ghostRenderer = ghostObject.AddComponent<MeshRenderer>();
        ghostRenderer.sharedMaterial = effectMaterial != null ? effectMaterial
            : ballRenderer != null ? ballRenderer.sharedMaterial
            : null;
        ghostRenderer.shadowCastingMode = ShadowCastingMode.Off;
        ghostRenderer.receiveShadows = false;

        ghostObject.AddComponent<DashGhost>().Play(transform.lossyScale, travelled * ghostStretch,
                                                   ghostLifetime, ghostCollapse, ghostThickness, ghostFade);
    }

    /// <summary>
    /// Throws the puff off the back of the ball, going the way the ball came. It leaves from the ball's
    /// trailing surface rather than its middle, so the particles read as shaken off the back of the
    /// streak instead of hanging in the space the ball used to fill.
    /// </summary>
    void EmitPuff(Vector3 position, Vector3 heading)
    {
        if (puff == null) return;

        Vector3 backside = position - heading * HalfLengthAlongDash();

        // The cone throws along the system's own Z, so point that back down the dash.
        puff.transform.SetPositionAndRotation(backside, Quaternion.FromToRotation(Vector3.forward, -heading));
        puff.Play();
        puff.Emit(Mathf.Max(0, puffCount));
    }

    /// <summary>Half the ball's own length along the dash: how far its back surface sits behind its middle.</summary>
    float HalfLengthAlongDash()
    {
        return ballMesh != null ? ballMesh.bounds.extents.z * Mathf.Abs(transform.lossyScale.z) : 0f;
    }

    /// <summary>
    /// Builds the puff the dash throws back. Nothing emits on its own - every particle comes from the
    /// Emit call a dash makes - so the system can sit playing quietly in between dashes.
    /// </summary>
    ParticleSystem BuildPuff()
    {
        var puffObject = new GameObject($"{name} Dash Puff");
        var system = puffObject.AddComponent<ParticleSystem>();

        // Adding the component starts it, and half of these settings refuse to change mid-play.
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = system.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 128;
        main.startLifetime = puffLifetime;
        main.startSpeed = new ParticleSystem.MinMaxCurve(puffSpeed * 0.6f, puffSpeed * 1.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(puffSize * 0.5f, puffSize);
        main.gravityModifier = 0f;

        // Driven by hand, so it has no rate and no bursts of its own to fire off unprompted.
        var emission = system.emission;
        emission.enabled = false;

        // A tight cone: the puff comes off the back of the ball, not out of its whole volume.
        var shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = puffSpread;
        shape.radius = 0.2f;

        // A mesh shader like this one ignores a particle's own colour, so the particles cannot fade out
        // one by one - they shrink away to nothing instead, which is the same read at this size.
        var size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

        var puffRenderer = system.GetComponent<ParticleSystemRenderer>();
        if (effectMaterial != null) puffRenderer.sharedMaterial = effectMaterial;
        puffRenderer.shadowCastingMode = ShadowCastingMode.Off;
        puffRenderer.receiveShadows = false;

        return system;
    }
}
