using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class Comic : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Full-screen Image component that displays comic frames.")]
    [SerializeField] private Image comicImage;

    [Header("Content")]
    [Tooltip("Ordered list of sprites for the comic pages (set in Inspector).")]
    [SerializeField] private List<Sprite> pages = new List<Sprite>();

    [Header("Controls")]
    [Tooltip("Key to advance to next page.")]
    [SerializeField] private KeyCode advanceKey = KeyCode.E;
    [Tooltip("Optional key to go back a page.")]
    [SerializeField] private KeyCode backKey = KeyCode.Q;
    [Tooltip("Optional key to skip to Hub.")]
    [SerializeField] private KeyCode skipKey = KeyCode.Escape;

    [Header("Flow")]
    [Tooltip("Scene to load after the last page.")]
    [SerializeField] private string hubSceneName = "Hub";

    [Header("Presentation")]
    [Tooltip("Fade duration between pages.")]
    [SerializeField] private float fadeDuration = 0.15f;
    [Tooltip("Color tint applied to the Image (use alpha 1 for full opacity).")]
    [SerializeField] private Color tintColor = Color.white;

    private int index = 0;
    private bool busy = false;

    private void Awake()
    {
        // Auto-find the Image if not assigned
        if (comicImage == null)
        {
            var found = GameObject.Find("ComicImage");
            if (found != null) comicImage = found.GetComponent<Image>();
        }

        if (comicImage == null)
        {
            Debug.LogError("Comic: No Image assigned/found. Please assign a UI Image (full-screen).");
            enabled = false;
            return;
        }

        comicImage.color = tintColor;
        ShowPage(0, instant:true);
    }

    private void Update()
    {
        if (busy) return;
        if (pages == null || pages.Count == 0) return;

        if (Input.GetKeyDown(advanceKey))
        {
            NextPage();
            return;
        }

        if (Input.GetKeyDown(backKey))
        {
            PrevPage();
            return;
        }

        if (Input.GetKeyDown(skipKey))
        {
            LoadHub();
        }
    }

    private void NextPage()
    {
        if (index < pages.Count - 1)
        {
            ShowPage(index + 1);
        }
        else
        {
            LoadHub();
        }
    }

    private void PrevPage()
    {
        if (index > 0)
            ShowPage(index - 1);
    }

    private void ShowPage(int newIndex, bool instant = false)
    {
        if (newIndex < 0 || newIndex >= pages.Count) return;
        index = newIndex;

        if (instant || fadeDuration <= 0f)
        {
            comicImage.sprite = pages[index];
            comicImage.SetNativeSize(); // optional if you want native size
            comicImage.preserveAspect = true;
            comicImage.rectTransform.anchorMin = Vector2.zero;
            comicImage.rectTransform.anchorMax = Vector2.one;
            comicImage.rectTransform.offsetMin = Vector2.zero;
            comicImage.rectTransform.offsetMax = Vector2.zero;
            return;
        }

        StartCoroutine(FadeToPage(pages[index]));
    }

    private IEnumerator FadeToPage(Sprite next)
    {
        busy = true;

        // Fade out
        float t = 0f;
        Color c = comicImage.color;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            c.a = Mathf.Lerp(1f, 0f, t / fadeDuration);
            comicImage.color = c;
            yield return null;
        }

        // Swap sprite
        comicImage.sprite = next;
        comicImage.preserveAspect = true;

        // Fade in
        t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            c.a = Mathf.Lerp(0f, 1f, t / fadeDuration);
            comicImage.color = c;
            yield return null;
        }

        busy = false;
    }

    private void LoadHub()
    {
        if (string.IsNullOrEmpty(hubSceneName))
        {
            Debug.LogError("Comic: hubSceneName is empty. Set it in the Inspector.");
            return;
        }
        SceneManager.LoadScene(hubSceneName, LoadSceneMode.Single);
    }
}
