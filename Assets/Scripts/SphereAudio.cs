using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// The rolling sphere's voice.
///
/// Three loops, all driven by the rigidbody rather than by input, so the sound reports what the
/// ball is actually doing - including the moments the player did not ask for:
///
///   Roll   ground contact. Pitch and brightness rise with travel speed; silent in the air, and
///          fully closed down when the ball is barely moving.
///   Drive  the drivetrain. Pitched over a much wider range than the roll, so the motor climbs
///          the way an electric vehicle does as the ball speeds up. It keeps running in the air,
///          because a spinning ball does not stop having a motor.
///   Slip   the contact patch sliding instead of rolling. This is the interesting one: a sphere
///          that is spun up faster than it is travelling is grinding its spin away against the
///          floor, and that is precisely what happens when the player accelerates in a direction
///          the ball is not already going.
///
/// Slip is measured two ways, because the game has two kinds of it. The velocity of the point of
/// the ball touching the ground is <c>v + w x r</c>; for a perfect roll that is zero, and whatever
/// is left is the contact patch sliding - a landing, a dash, a shove. But Unity's contact solver
/// holds a rolling sphere to no-slip so well that even a hard cut reads zero there, while the ball
/// is plainly scrubbing around the corner. So the drive direction is compared with the direction of
/// travel as well: accelerating across the motion costs grip, and that is the slip the player
/// actually feels. The larger of the two drives the layer.
///
/// Collisions are one-shots on top: the closing speed along the contact normal picks a heavy or a
/// medium take and scales it, so a nudge into a wall is a soft knock and a full-speed hit lands.
/// They go through <see cref="AudioManager"/> so the ball's three loops - which live on their own
/// sources - are never the voice the pool recycles.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class SphereAudio : MonoBehaviour
{
    [Header("Clips")]
    [Tooltip("Ground-contact loop: rolling mass, no motor.")]
    [SerializeField] AudioClip rollClip;
    [Tooltip("Drivetrain loop: the electric motor and its switching whine.")]
    [SerializeField] AudioClip driveClip;
    [Tooltip("Traction-loss loop: what the contact patch sounds like when it is sliding.")]
    [SerializeField] AudioClip slipClip;

    [Header("Mix")]
    [Tooltip("Bus all three loops play on. Empty sends them to the master.")]
    [SerializeField] AudioMixerGroup output;
    [Tooltip("Level of the ground layer at full speed, before the mixer.")]
    [Range(0f, 1f)]
    [SerializeField] float rollVolume = 0.2f;
    [Tooltip("Level of the drivetrain at full speed. Kept under the roll: it is a tone, and a tone " +
             "reads louder than noise at the same level.")]
    [Range(0f, 1f)]
    [SerializeField] float driveVolume = 0.12f;
    [Tooltip("Level of the slip layer at full slip.")]
    [Range(0f, 1f)]
    [SerializeField] float slipVolume = 0.28f;

    [Header("Impacts")]
    [Tooltip("Heavy collision takes. One is picked at random per hit.")]
    [SerializeField] AudioClip[] heavyImpacts;
    [Tooltip("Medium collision takes, for knocks and landings below the heavy threshold.")]
    [SerializeField] AudioClip[] mediumImpacts;
    [Tooltip("Bus the collision one-shots play on.")]
    [SerializeField] AudioMixerGroup impactOutput;
    [Tooltip("Level of a full-strength impact, before the mixer.")]
    [Range(0f, 1f)]
    [SerializeField] float impactVolume = 0.45f;
    [Tooltip("Closing speed along the contact normal, in metres per second, below which a contact " +
             "is a roll over a seam rather than a hit.")]
    [SerializeField] float minImpactSpeed = 1.5f;
    [Tooltip("Closing speed above which the heavy takes are used instead of the medium ones.")]
    [SerializeField] float heavyImpactSpeed = 5f;
    [Tooltip("Closing speed at which an impact plays at full level.")]
    [SerializeField] float fullImpactSpeed = 12f;
    [Tooltip("Seconds before another collision on this ball can make a sound. Keeps a multi-contact " +
             "hit from stacking several takes on the same frame.")]
    [SerializeField] float impactCooldown = 0.08f;
    [Tooltip("Random playback-rate range for impacts, so repeated hits do not sound identical.")]
    [SerializeField] Vector2 impactPitch = new Vector2(0.92f, 1.08f);

    [Header("Speed")]
    [Tooltip("Travel speed, in metres per second, at which the roll and drive are at full song.")]
    [SerializeField] float fullSpeed = 14f;
    [Tooltip("Contact-patch speed, in metres per second, below which the ball still counts as " +
             "gripping. Physics leaves a little creep even in a clean roll, so this is not zero.")]
    [SerializeField] float slipStart = 1.0f;
    [Tooltip("Contact-patch speed at which the slip layer is at full level.")]
    [SerializeField] float slipFull = 9f;
    [Tooltip("How much of a sideways drive command counts as scrubbing. A ball asked to go 90 " +
             "degrees across its travel scrubs half its speed times this; asked to reverse, all " +
             "of it.")]
    [SerializeField] float scrubGain = 1.4f;
    [Tooltip("The driver whose input says which way the ball is being pushed. Found on the same " +
             "object when left empty; a sphere with no driver reads only real contact skidding.")]
    [SerializeField] Player driver;

    [Header("Pitch")]
    [Tooltip("Roll playback rate when stopped and at full speed.")]
    [SerializeField] Vector2 rollPitch = new Vector2(0.8f, 1.15f);
    [Tooltip("Drive playback rate when stopped and at full speed. The wide span is what makes the " +
             "motor climb an octave as the ball accelerates.")]
    [SerializeField] Vector2 drivePitch = new Vector2(0.45f, 1.85f);
    [Tooltip("Slip playback rate at the start of a slip and at full slip.")]
    [SerializeField] Vector2 slipPitch = new Vector2(0.9f, 1.6f);

    [Header("Tone")]
    [Tooltip("Low-pass on the roll when stopped and at full speed. A slow ball is dull; a fast one " +
             "opens up.")]
    [SerializeField] Vector2 rollCutoff = new Vector2(700f, 18000f);
    [Tooltip("High-pass on the drive, so the motor does not turn into sub-bass when it is pitched " +
             "right down at low speed.")]
    [SerializeField] float driveHighPass = 45f;

    [Header("Placement")]
    [Tooltip("Distance at which the ball is at full level. It is a big object, and the third-person " +
             "camera never gets closer than a few metres.")]
    [SerializeField] float minDistance = 2f;
    [Tooltip("Distance at which the ball has faded out entirely.")]
    [SerializeField] float maxDistance = 50f;
    [Tooltip("What counts as ground. Everything is probed by default; put the ball's own layer " +
             "outside this mask if it has one.")]
    [SerializeField] LayerMask groundMask = ~0;

    [Header("Tuning aid")]
    [Tooltip("Log the measured speeds and the levels they produce once a second. Left off in play.")]
    [SerializeField] bool logState;

    Rigidbody body;
    SphereCollider ball;
    float radius = 0.5f;

    AudioSource rollSource, driveSource, slipSource;
    AudioLowPassFilter rollFilter;

    float roll01;
    float slip01;
    float throttle01;
    bool grounded;
    bool slipping;
    float nextImpactTime;

    /// <summary>Travel speed over the ground, in metres per second.</summary>
    public float RollSpeed { get; private set; }

    /// <summary>
    /// How hard the contact patch is being scrubbed, in metres per second. Zero is a clean roll.
    /// This is the larger of the real contact skid and the scrub the driver's input is asking for
    /// - see <see cref="Measure"/>.
    /// </summary>
    public float SlipSpeed { get; private set; }

    /// <summary>The real contact-point skid, before the drive-direction term is folded in.</summary>
    public float SkidSpeed { get; private set; }

    /// <summary>The scrub implied by driving across the direction of travel.</summary>
    public float ScrubSpeed { get; private set; }

    /// <summary>Whether the ball is touching a floor this frame.</summary>
    public bool Grounded => grounded;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        ball = GetComponent<SphereCollider>();
        if (driver == null) driver = GetComponent<Player>();

        // The sphere collider is in local space; the ball's transform scales it.
        if (ball != null)
        {
            Vector3 scale = transform.lossyScale;
            radius = ball.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        }
        else
        {
            // No collider to ask: fall back to the rendered size, which is what the sound sits on.
            var mesh = GetComponent<MeshFilter>();
            if (mesh != null && mesh.sharedMesh != null)
            {
                Vector3 size = mesh.sharedMesh.bounds.size;
                radius = Mathf.Max(size.x, size.y, size.z) * 0.5f * transform.lossyScale.x;
            }
        }

        rollSource = BuildVoice("Roll Voice", rollClip);
        driveSource = BuildVoice("Drive Voice", driveClip);
        slipSource = BuildVoice("Slip Voice", slipClip);

        // Only the ground layer gets a moving filter. Closing it down at low speed is what stops a
        // slow roll sounding like the same recording played quietly.
        rollFilter = rollSource.gameObject.AddComponent<AudioLowPassFilter>();
        rollFilter.cutoffFrequency = rollCutoff.x;

        var driveFilter = driveSource.gameObject.AddComponent<AudioHighPassFilter>();
        driveFilter.cutoffFrequency = driveHighPass;

        rollSource.Play();
        driveSource.Play();
        slipSource.Play();

        // Start the three loops at unrelated points. In sync they would sound like one clip with a
        // filter on it; offset, they read as three independent mechanisms.
        rollSource.time = 0f;
        driveSource.time = driveClip != null ? driveClip.length * 0.37f : 0f;
        slipSource.time = 0f;
    }

    void OnDisable()
    {
        if (rollSource != null) rollSource.Stop();
        if (driveSource != null) driveSource.Stop();
        if (slipSource != null) slipSource.Stop();
    }

    void OnEnable()
    {
        // Awake has not run on the first enable, and there is nothing to restart yet.
        if (rollSource == null) return;

        rollSource.Play();
        driveSource.Play();
        slipSource.Play();
    }

    void Update()
    {
        Measure();

        // Onset is followed quickly and release slowly: a slip that starts should be audible at
        // once, while the end of one should tail off rather than cut.
        float dt = Time.deltaTime;
        roll01 = Approach(roll01, Mathf.Clamp01(RollSpeed / Mathf.Max(fullSpeed, 0.01f)), dt, 0.12f, 0.22f);
        slip01 = Approach(slip01, SlipTarget(), dt, 0.035f, 0.16f);

        // The motor follows the throttle, not the speedometer. A ball that is still coasting has
        // its motor off; only the ground layer should be audible then. The slower release lets
        // the drive spin down instead of cutting when the stick comes back to centre.
        throttle01 = Approach(throttle01, driver != null ? driver.DriveThrottle : 0f, dt, 0.05f, 0.28f);

        Apply();

        if (logState && Time.time - lastLog > 1f)
        {
            lastLog = Time.time;
            Debug.Log($"[SphereAudio] roll={RollSpeed:F2} m/s slip={SlipSpeed:F2} m/s " +
                      $"(skid={SkidSpeed:F2} scrub={ScrubSpeed:F2}) " +
                      $"grounded={grounded} roll01={roll01:F2} slip01={slip01:F2} " +
                      $"levels={rollSource.volume:F2}/{driveSource.volume:F2}/{slipSource.volume:F2} " +
                      $"pitch={rollSource.pitch:F2}/{driveSource.pitch:F2}/{slipSource.pitch:F2}");
        }
    }

    float lastLog;

    /// <summary>
    /// A collision makes a one-shot, scaled by how hard the ball actually met whatever it hit.
    ///
    /// <c>collision.relativeVelocity</c> is the whole relative velocity, which on a glancing pass is
    /// large even when the ball barely touched anything; the closing speed along the contact normal
    /// is what separates a scrape from a hit. The strongest contact on the frame picks both the
    /// level and the sound the hit is placed at.
    /// </summary>
    void OnCollisionEnter(Collision collision)
    {
        if (Time.time < nextImpactTime) return;

        int contacts = collision.contactCount;
        if (contacts == 0) return;

        float closing = 0f;
        Vector3 point = collision.GetContact(0).point;

        for (int i = 0; i < contacts; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            // Absolute value: which way Unity reports the relative velocity against the contact
            // normal is not something to hinge an impact on. Only the component along the normal
            // is a hit; the rest is a scrape.
            float speed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
            if (speed <= closing) continue;

            closing = speed;
            point = contact.point;
        }

        if (logState)
        {
            Debug.Log($"[SphereAudio] contact {collision.collider.name} " +
                      $"rv={collision.relativeVelocity.magnitude:F2} closing={closing:F2}");
        }

        if (closing < minImpactSpeed) return;

        bool heavy = closing >= heavyImpactSpeed;
        AudioClip[] takes = heavy ? heavyImpacts : mediumImpacts;
        if (takes == null || takes.Length == 0) return;

        AudioClip clip = takes[Random.Range(0, takes.Length)];
        if (clip == null) return;

        float strength = Mathf.InverseLerp(minImpactSpeed, fullImpactSpeed, closing);
        float volume = impactVolume * Mathf.Lerp(0.35f, 1f, strength);
        float pitch = Random.Range(impactPitch.x, impactPitch.y);

        AudioManager.PlayOneShot(clip, point, impactOutput, volume, pitch);
        nextImpactTime = Time.time + impactCooldown;

        if (logState)
        {
            Debug.Log($"[SphereAudio] impact {closing:F1} m/s {(heavy ? "heavy" : "medium")} " +
                      $"{clip.name} vol={volume:F2} pitch={pitch:F2}");
        }
    }

    /// <summary>
    /// Reads the rigidbody and works out how badly the contact patch is being scrubbed.
    ///
    /// There are two ways that happens, and the game needs both:
    ///
    /// 1. Real skidding. The velocity of the point of the ball touching the ground is
    ///    <c>v + w x r</c>; for a perfect roll that is zero. Whatever is left after removing the
    ///    component along the normal is the contact patch sliding. This is what fires when the
    ///    ball is dropped, dashes, or is shoved while its spin says otherwise.
    ///
    /// 2. Being driven across the way the ball is already travelling. Unity's contact solver
    ///    holds a rolling sphere almost perfectly to no-slip, so a hard cut across the input
    ///    reads as zero on the first measure even though the ball is visibly scrubbing its way
    ///    around the corner. So the input's drive direction is compared with the direction of
    ///    travel: push along the motion and nothing slips, push across it and the ball is
    ///    grinding sideways until its travel catches up with the command. That is the case the
    ///    sound is really for.
    ///
    /// Both are in metres per second and the larger wins.
    /// </summary>
    void Measure()
    {
        Vector3 normal = Vector3.up;
        grounded = false;

        Vector3 origin = body != null ? body.position : transform.position;

        // A ray from the centre down, just past the surface. Starting inside the ball's own collider
        // means that collider is not what gets hit.
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, radius + 0.15f, groundMask,
                            QueryTriggerInteraction.Ignore))
        {
            grounded = true;
            normal = hit.normal;
        }

        Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
        Vector3 spin = body != null ? body.angularVelocity : Vector3.zero;

        Vector3 travel = Vector3.ProjectOnPlane(velocity, normal);
        RollSpeed = travel.magnitude;

        if (!grounded)
        {
            SlipSpeed = 0f;
            SkidSpeed = 0f;
            ScrubSpeed = 0f;
            return;
        }

        // 1. Real contact skid.
        Vector3 contactVelocity = velocity + Vector3.Cross(spin, -normal * radius);
        Vector3 skid = Vector3.ProjectOnPlane(contactVelocity, normal);

        // 2. Drive direction against travel direction.
        float scrub = 0f;
        if (driver != null && RollSpeed > 0.05f)
        {
            Vector3 wanted = Vector3.ProjectOnPlane(driver.DriveDirection, normal);
            if (wanted.sqrMagnitude > 1e-4f)
            {
                float alignment = Vector3.Dot(wanted.normalized, travel / RollSpeed);
                float across = (1f - alignment) * 0.5f;      // 0 along the motion, 1 against it
                scrub = RollSpeed * across * scrubGain;
            }
        }

        SkidSpeed = skid.magnitude;
        ScrubSpeed = scrub;
        SlipSpeed = Mathf.Max(SkidSpeed, ScrubSpeed);
    }

    /// <summary>0-1 for how much of the ball's contact is skidding rather than rolling.</summary>
    float SlipTarget()
    {
        if (!grounded) return 0f;

        float t = Mathf.InverseLerp(slipStart, slipFull, SlipSpeed);
        // A little curve on the way in: the first scrapes are quiet, and the last of the grip
        // going is loud. Linear reads as an on/off switch at the bottom of the range.
        return Mathf.Pow(t, 0.85f);
    }

    void Apply()
    {
        // Ground: gone in the air, dull and quiet at a crawl, open and loud at speed.
        float groundGate = grounded ? 1f : 0f;
        rollSource.volume = rollVolume * Mathf.Pow(roll01, 0.5f) * groundGate;
        rollSource.pitch = Mathf.Lerp(rollPitch.x, rollPitch.y, roll01);
        rollFilter.cutoffFrequency = Mathf.Lerp(rollCutoff.x, rollCutoff.y, roll01);

        // Drive: only while the player is asking for it. It still pitches with travel speed, and
        // it keeps running in the air, because a ball being driven in mid-air has a motor too.
        driveSource.volume = driveVolume * throttle01 * Mathf.Lerp(0.55f, 1f, roll01);
        driveSource.pitch = Mathf.Lerp(drivePitch.x, drivePitch.y, roll01);

        // Slip: only against the ground, and louder the worse the skid.
        slipSource.volume = slipVolume * slip01;
        slipSource.pitch = Mathf.Lerp(slipPitch.x, slipPitch.y, slip01);

        // A fresh slip starts somewhere else in the clip, so a player who keeps cutting the same
        // corner does not get the same four seconds of scraping every time.
        bool nowSlipping = slip01 > 0.02f;
        if (nowSlipping && !slipping && slipClip != null)
        {
            slipSource.time = Random.value * slipClip.length;
        }

        slipping = nowSlipping;
    }

    /// <summary>
    /// One-pole approach with separate attack and release times, frame-rate independent.
    /// </summary>
    static float Approach(float current, float target, float dt, float attack, float release)
    {
        float tau = target > current ? attack : release;
        float k = 1f - Mathf.Exp(-dt / Mathf.Max(tau, 0.0001f));
        return Mathf.Lerp(current, target, k);
    }

    /// <summary>
    /// Builds one of the three voices. They are children of the ball so they travel with it, and
    /// deliberately not borrowed from <see cref="AudioManager"/>: a pool hands out its oldest voice
    /// to the next one-shot, and a rolling ball must not go quiet because someone fired a gun.
    /// </summary>
    AudioSource BuildVoice(string voiceName, AudioClip clip)
    {
        var child = new GameObject(voiceName);
        child.transform.SetParent(transform, false);

        var source = child.AddComponent<AudioSource>();
        source.clip = clip;
        source.playOnAwake = false;
        source.loop = true;
        source.volume = 0f;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;      // pitch is driven by hand above; doppler would fight it
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.outputAudioMixerGroup = output;
        return source;
    }
}
