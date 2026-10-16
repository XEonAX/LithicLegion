using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// The game's audio output: a small pool of <see cref="AudioSource"/>s anyone can borrow to play a
/// one-shot somewhere in the world, plus the mixer those voices feed.
///
/// Nothing has to own an AudioSource to make a noise. A gun, an impact or a dash just says what to
/// play, where it happened, and which bus it belongs on - so a sound with no object to live on still
/// gets a position and a volume slider. Voices are handed out in turn, which means the one reused is
/// always the one that has been sounding longest: a burst of shots thins out at the edges instead of
/// cutting into a sound that just started.
/// </summary>
public class AudioManager : MonoBehaviour
{
    [Header("Mixer")]
    [Tooltip("The game's mixer. Its exposed volume parameters are what SetVolume drives.")]
    [SerializeField] AudioMixer mixer;

    [Header("Voices")]
    [Tooltip("How many one-shots can sound at once. Past this the longest-running voice is reused.")]
    [SerializeField] int voiceCount = 24;

    /// <summary>The manager in this scene, or null when the scene has none.</summary>
    public static AudioManager Instance { get; private set; }

    AudioSource[] voices;
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
        BuildVoices();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void BuildVoices()
    {
        voices = new AudioSource[Mathf.Max(voiceCount, 1)];

        for (int i = 0; i < voices.Length; i++)
        {
            AudioSource voice = new GameObject($"Voice {i}").AddComponent<AudioSource>();
            voice.transform.SetParent(transform, false);
            voice.playOnAwake = false;
            voice.spatialBlend = 1f;   // 3D: a shot comes from the muzzle, not from the camera
            voices[i] = voice;
        }
    }

    /// <summary>
    /// Plays <paramref name="clip"/> once, at <paramref name="position"/>, through
    /// <paramref name="group"/>. A missing manager or clip drops the sound instead of throwing, so an
    /// unfinished audio setup never breaks the thing that was making the noise.
    /// </summary>
    public static void PlayOneShot(AudioClip clip, Vector3 position, AudioMixerGroup group = null,
                                   float volume = 1f, float pitch = 1f)
    {
        if (clip == null || Instance == null) return;

        Instance.Play(clip, position, group, volume, pitch);
    }

    void Play(AudioClip clip, Vector3 position, AudioMixerGroup group, float volume, float pitch)
    {
        AudioSource voice = voices[nextVoice];
        nextVoice = (nextVoice + 1) % voices.Length;

        // A reused voice stops whatever it was still playing - that sound is the oldest one here.
        voice.Stop();
        voice.transform.position = position;
        voice.outputAudioMixerGroup = group;
        voice.volume = volume;
        voice.pitch = pitch;
        voice.clip = clip;
        voice.Play();
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
