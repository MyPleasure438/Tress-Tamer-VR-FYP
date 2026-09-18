using UnityEngine;

// HANDOFF NOTE:
// Simple reusable audio helper - a quick 3D positioned click sound for any button (call PlayClickSound at
// the button's world position), plus optional continuous looping background music. Self-contained, does
// not touch any existing script - other scripts just call UIAudioManager.Instance?.PlayClickSound(pos).
//
// HOW TO WIRE THIS UP IN UNITY:
// 1. Create an empty GameObject (e.g. 'UI Audio Manager') anywhere in the scene, add this component.
// 2. Assign a Click Sound clip (any short UI click/tap sound).
// 3. Optional: assign a Background Music clip and check Play Music On Start for continuous looping music.
[DisallowMultipleComponent]
public class UIAudioManager : MonoBehaviour
{
    public static UIAudioManager Instance { get; private set; }

    [Header("Button Click Sound (3D, one-shot)")]
    [SerializeField] private AudioClip clickSound;
    [Range(0f, 1f)]
    [SerializeField] private float clickVolume = 0.7f;
    [Tooltip("How far the click sound can be heard, in meters.")]
    [SerializeField] private float clickMaxDistance = 8f;

    [Header("Background Music (looping)")]
    [SerializeField] private AudioClip backgroundMusic;
    [Range(0f, 1f)]
    [SerializeField] private float musicVolume = 0.3f;
    [SerializeField] private bool playMusicOnStart = false;
    [Tooltip("0 = fully 2D (heard everywhere equally), 1 = fully 3D positional. Music is usually kept closer to 0 so it doesn't fade out as the player walks around.")]
    [Range(0f, 1f)]
    [SerializeField] private float musicSpatialBlend = 0.1f;

    private AudioSource musicSource;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.loop = true;
        musicSource.playOnAwake = false;
        musicSource.spatialBlend = musicSpatialBlend;
        musicSource.volume = musicVolume;
    }

    private void Start()
    {
        if (playMusicOnStart && backgroundMusic != null)
            PlayMusic(backgroundMusic);
    }

    // Call this at a button's world position when it's clicked - creates a brief temporary 3D AudioSource,
    // plays the click sound once, then cleans itself up automatically.
    public void PlayClickSound(Vector3 worldPosition)
    {
        if (clickSound == null)
            return;

        GameObject temp = new GameObject("ClickSound_Temp");
        temp.transform.position = worldPosition;

        AudioSource source = temp.AddComponent<AudioSource>();
        source.clip = clickSound;
        source.volume = clickVolume;
        source.spatialBlend = 1f; // fully 3D positional, per request
        source.maxDistance = clickMaxDistance;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.Play();

        Destroy(temp, clickSound.length + 0.1f);
    }

    public void PlayMusic(AudioClip clip)
    {
        if (musicSource == null || clip == null)
            return;

        musicSource.clip = clip;
        musicSource.Play();
    }

    public void StopMusic()
    {
        if (musicSource != null)
            musicSource.Stop();
    }
}
