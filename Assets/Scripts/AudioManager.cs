using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// The game's audio output: a small pool of <see cref="AudioSource"/>s anyone can borrow to play a
/// one-shot somewhere in the world, plus the mixer those voices feed.
///
/// Nothing has to own an AudioSource to make a noise. A gun, an impact or a dash just says what to
/// play, where it happened, and which bus it belongs on - so a sound with no object to live on still
/// gets a position and a volume slider. A fire-and-forget sound goes through
/// <see cref="PlayOneShot"/>; one that has to follow or stop with something - a charge that dies with
/// the trigger, a beam on the muzzle - goes through <see cref="Play"/> and keeps the returned handle.
/// Voices are handed out in turn, which means the one reused is always the one that has been sounding
/// longest: a burst of shots thins out at the edges instead of cutting into a sound that just started.
/// </summary>
public class AudioManager : MonoBehaviour
{
    [Header("Mixer")]
    [Tooltip("The game's mixer. Its exposed volume parameters are what SetVolume drives.")]
    [SerializeField] AudioMixer mixer;

    [Header("Voices")]
    [Tooltip("How many sounds can overlap. Past this the longest-running voice is reused.")]
    [SerializeField] int voiceCount = 24;

    /// <summary>The manager in this scene, or null when the scene has none.</summary>
    public static AudioManager Instance { get; private set; }

    AudioSource[] voices;
    Voice[] borrowed;
    AudioMixerGroup masterGroup;
    int nextVoice;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"{name}: a second AudioManager is already in the scene, so this one " +
                             "will not play anything.", this);
            enabled = false;
            return;
        }

        Instance = this;
        masterGroup = FindMasterGroup();
        BuildVoices();
    }

    AudioMixerGroup FindMasterGroup()
    {
        if (mixer == null) return null;

        AudioMixerGroup[] groups = mixer.FindMatchingGroups("Master");
        return groups.Length > 0 ? groups[0] : null;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void BuildVoices()
    {
        voices = new AudioSource[Mathf.Max(voiceCount, 1)];
        borrowed = new Voice[voices.Length];

        for (int i = 0; i < voices.Length; i++)
        {
            AudioSource voice = new GameObject($"Voice {i}").AddComponent<AudioSource>();
            voice.transform.SetParent(transform, false);
            voice.playOnAwake = false;
            voice.spatialBlend = 1f;   // 3D: a shot comes from the muzzle, not from the camera
            voices[i] = voice;
        }
    }

    void Update()
    {
        // A duplicate manager is disabled in Awake before it builds any voices, and a domain reload
        // in play mode can leave the arrays unbuilt for a frame. Neither should throw.
        if (borrowed == null) return;

        // A one-shot is borrowed until it finishes; a loop is borrowed until its caller stops it.
        for (int i = 0; i < borrowed.Length; i++)
        {
            Voice voice = borrowed[i];
            if (voice != null && !voice.IsPlaying) borrowed[i] = null;
        }
    }

    /// <summary>
    /// Plays <paramref name="clip"/> once, at <paramref name="position"/>, through
    /// <paramref name="group"/>. A missing manager or clip drops the sound instead of throwing, so an
    /// unfinished audio setup never breaks the thing that was making the noise.
    ///
    /// <paramref name="spatialBlend"/> is 1 for a sound that lives in the world and 0 for one that is
    /// always at the listener. A weapon the player is holding wants close to 0: at a third-person
    /// camera distance, full 3D and Unity's 1 m rolloff would fade it by twenty-odd decibels.
    /// </summary>
    public static void PlayOneShot(AudioClip clip, Vector3 position, AudioMixerGroup group = null,
                                   float volume = 1f, float pitch = 1f, float spatialBlend = 1f)
    {
        if (clip == null || Instance == null) return;

        Instance.StartVoice(clip, position, group, volume, pitch, false, spatialBlend);
    }

    /// <summary>
    /// Starts a sound and hands back the voice playing it, so a caller that needs to stop or move a
    /// sound - a charge that dies with the trigger, a beam that follows the muzzle - can. A sound
    /// played through <see cref="PlayOneShot"/> is the same thing with the handle thrown away.
    /// </summary>
    public static Voice Play(AudioClip clip, Vector3 position, AudioMixerGroup group = null,
                             float volume = 1f, float pitch = 1f, bool loop = false,
                             float spatialBlend = 1f)
    {
        if (clip == null || Instance == null) return null;

        return Instance.StartVoice(clip, position, group, volume, pitch, loop, spatialBlend);
    }

    Voice StartVoice(AudioClip clip, Vector3 position, AudioMixerGroup group, float volume, float pitch,
                     bool loop, float spatialBlend)
    {
        int slot = TakeVoice();
        AudioSource voice = voices[slot];

        // A reused voice stops whatever it was still playing - that sound is the oldest one here.
        voice.Stop();
        voice.transform.position = position;
        voice.outputAudioMixerGroup = group != null ? group : masterGroup;
        voice.volume = volume;
        voice.pitch = pitch;
        voice.loop = loop;
        voice.spatialBlend = Mathf.Clamp01(spatialBlend);
        voice.clip = clip;
        voice.Play();

        Voice handle = new Voice(this, voice);
        borrowed[slot] = handle;
        return handle;
    }

    /// <summary>The free voice that has been waiting longest, or the oldest sound when all are busy.</summary>
    int TakeVoice()
    {
        for (int i = 0; i < voices.Length; i++)
        {
            int index = (nextVoice + i) % voices.Length;
            if (borrowed[index] != null) continue;

            nextVoice = (index + 1) % voices.Length;
            return index;
        }

        int oldest = nextVoice;
        nextVoice = (nextVoice + 1) % voices.Length;
        borrowed[oldest] = null;
        return oldest;
    }

    internal void Release(Voice voice)
    {
        for (int i = 0; i < borrowed.Length; i++)
        {
            if (borrowed[i] != voice) continue;

            borrowed[i] = null;
            return;
        }
    }

    /// <summary>
    /// A sound the manager is playing. The handle is only valid until the voice is borrowed again by
    /// a burst that exhausts the pool; it is meant to outlive one charge, not one whole scene.
    /// </summary>
    public sealed class Voice
    {
        readonly AudioManager owner;
        readonly AudioSource source;

        internal Voice(AudioManager owner, AudioSource source)
        {
            this.owner = owner;
            this.source = source;
        }

        /// <summary>Whether this voice is still sounding. False once it has ended or been stopped.</summary>
        public bool IsPlaying => source != null && source.isPlaying;

        /// <summary>Seconds into the clip, for a caller that wants to follow the sound rather than the clock.</summary>
        public float PlaybackTime => source != null ? source.time : 0f;

        /// <summary>Moves the sound. A charge that follows the muzzle calls this every frame.</summary>
        public void SetPosition(Vector3 position)
        {
            if (source != null) source.transform.position = position;
        }

        public void Stop()
        {
            if (source != null)
            {
                source.Stop();
                source.loop = false;
            }

            if (owner != null) owner.Release(this);
        }
    }

    /// <summary>
    /// Sets an exposed volume on the game's mixer from a 0-1 slider position. The mixer thinks in
    /// decibels, so the conversion lives here instead of in every slider that wants to drive it.
    /// </summary>
    public static void SetVolume(string exposedParameter, float linear)
    {
        if (Instance == null || Instance.mixer == null) return;

        Instance.mixer.SetFloat(exposedParameter, LinearToDecibels(linear));
    }

    /// <summary>
    /// 0-1 as decibels. -80 dB is silence as far as the mixer is concerned, and log10 hits it at zero,
    /// so the floor is handled explicitly rather than by feeding the mixer an infinity.
    /// </summary>
    public static float LinearToDecibels(float linear)
    {
        return linear <= 0.0001f ? -80f : Mathf.Log10(Mathf.Clamp01(linear)) * 20f;
    }
}
