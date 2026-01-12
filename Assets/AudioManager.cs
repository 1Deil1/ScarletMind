using UnityEngine;

[DisallowMultipleComponent]
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Defaults")]
    [SerializeField] private float defaultVolume = 1f;

    private AudioSource sfx2D; // non-spatial, for UI/flat SFX

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        sfx2D = gameObject.AddComponent<AudioSource>();
        sfx2D.playOnAwake = false;
        sfx2D.loop = false;
        sfx2D.spatialBlend = 0f; // 2D
        sfx2D.volume = defaultVolume;
    }

    public static void PlaySfx2D(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (Instance == null || clip == null) return;
        Instance.sfx2D.pitch = pitch;
        Instance.sfx2D.PlayOneShot(clip, volume);
    }

    public static void PlaySfxAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
    {
        if (clip == null) return;
        var go = new GameObject("SFX_" + clip.name);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = false;
        src.clip = clip;
        src.volume = volume;
        src.pitch = pitch;
        src.spatialBlend = 1f; // 3D in world
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = 2f;
        src.maxDistance = 20f;
        go.transform.position = position;
        src.Play();
        Object.Destroy(go, clip.length / Mathf.Max(0.01f, Mathf.Abs(pitch)));
    }
}
