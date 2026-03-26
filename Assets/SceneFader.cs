using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Persistent singleton that fades the screen to black between scene loads.
/// Usage: SceneFader.LoadScene("SceneName") — call instead of SceneManager.LoadScene directly.
/// Drop this on any persistent GameObject in your first scene (e.g. a "Managers" object).
/// It creates its own Canvas and overlay automatically — no extra setup needed.
/// </summary>
[DisallowMultipleComponent]
public class SceneFader : MonoBehaviour
{
    public static SceneFader Instance { get; private set; }

    [Header("Fade Timing")]
    [Tooltip("How long the fade-OUT to black takes (seconds). Happens before the new scene loads.")]
    [SerializeField] private float fadeOutDuration = 0.4f;
    [Tooltip("How long the fade-IN from black takes (seconds). Happens after the new scene is ready.")]
    [SerializeField] private float fadeInDuration  = 0.5f;

    [Header("Appearance")]
    [Tooltip("Color of the full-screen overlay. Black is standard; try dark red for horror.")]
    [SerializeField] private Color fadeColor = Color.black;

    private Image overlay;
    private bool  isFading;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildOverlay();
        SceneManager.sceneLoaded += OnSceneLoaded;

        StartCoroutine(FadeIn()); // Fade in immediately on first scene
    }

    private void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // ?? Public API ????????????????????????????????????????????????

    /// <summary>
    /// Load a scene with a fade-out ? load ? fade-in transition.
    /// Drop-in replacement for SceneManager.LoadScene.
    /// </summary>
    public static void LoadScene(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
    {
        if (Instance == null)
        {
            // Fallback if the fader was never placed in the scene
            SceneManager.LoadScene(sceneName, mode);
            return;
        }
        Instance.StartCoroutine(Instance.FadeAndLoad(sceneName, mode));
    }

    // ?? Internal ??????????????????????????????????????????????????

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Always kick off a fade-in when a new scene is ready
        StartCoroutine(FadeIn());
    }

    private IEnumerator FadeAndLoad(string sceneName, LoadSceneMode mode)
    {
        if (isFading) yield break;

        yield return StartCoroutine(FadeOut());
        SceneManager.LoadScene(sceneName, mode);
        // FadeIn is triggered automatically by OnSceneLoaded
    }

    private IEnumerator FadeOut()
    {
        isFading = true;
        overlay.enabled = true;
        float elapsed = 0f;
        float duration = Mathf.Max(fadeOutDuration, 0.001f);
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetAlpha(Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        SetAlpha(1f);
        // NOTE: keep isFading = true here so nothing else fires between FadeOut and FadeIn
    }

    private IEnumerator FadeIn()
    {
        // Ensure we start fully black even if FadeOut wasn't called (e.g. first scene)
        overlay.enabled = true;
        SetAlpha(1f);
        isFading = true;

        float elapsed = 0f;
        float duration = Mathf.Max(fadeInDuration, 0.001f);
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetAlpha(1f - Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        SetAlpha(0f);
        overlay.enabled = false;
        isFading = false;
    }

    private void SetAlpha(float a)
    {
        Color c  = fadeColor;
        c.a      = a;
        overlay.color = c;
    }

    private void BuildOverlay()
    {
        var canvasGO = new GameObject("SceneFaderCanvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999; // always on top of everything
        canvasGO.AddComponent<CanvasScaler>();

        var imgGO = new GameObject("FadeOverlay");
        imgGO.transform.SetParent(canvasGO.transform, false);
        overlay = imgGO.AddComponent<Image>();
        overlay.raycastTarget = false;
        var rt = overlay.rectTransform;
        rt.anchorMin  = Vector2.zero;
        rt.anchorMax  = Vector2.one;
        rt.offsetMin  = Vector2.zero;
        rt.offsetMax  = Vector2.zero;

        SetAlpha(0f);
        overlay.enabled = false;
    }
}
