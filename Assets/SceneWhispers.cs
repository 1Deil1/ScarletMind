using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

[DisallowMultipleComponent]
public class SceneWhispers : MonoBehaviour
{
    [SerializeField] private string sceneName = "House";
    [SerializeField] private AudioClip whisperLoop;
    [SerializeField] private float fadeTime = 0.75f;
    [Range(0f,1f)] [SerializeField] private float targetVolume = 0.5f;

    private AudioSource src;
    private Coroutine fadeCo;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        src = gameObject.AddComponent<AudioSource>();
        src.loop = true; src.playOnAwake = false; src.spatialBlend = 0f; src.volume = 0f;

        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private void OnDestroy() { SceneManager.sceneLoaded -= OnSceneLoaded; }

    private void OnSceneLoaded(Scene scene, LoadSceneMode _)
    {
        bool isTarget = scene.name == sceneName;
        if (fadeCo != null) StopCoroutine(fadeCo);
        fadeCo = StartCoroutine(FadeTo(isTarget ? targetVolume : 0f, isTarget));
    }

    private IEnumerator FadeTo(float vol, bool ensureClip)
    {
        if (ensureClip && src.clip != whisperLoop) src.clip = whisperLoop;
        if (ensureClip && !src.isPlaying) src.Play();

        float start = src.volume, t = 0f;
        while (t < fadeTime) { t += Time.deltaTime; src.volume = Mathf.Lerp(start, vol, t / fadeTime); yield return null; }
        src.volume = vol;
        if (src.volume <= 0.001f && src.isPlaying) src.Stop();
    }
}
