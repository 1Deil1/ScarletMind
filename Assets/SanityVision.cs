using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways] // allow setup in Edit Mode
public class SanityVision : MonoBehaviour
{
    private static SanityVision _instance;

    [Header("References")]
    [SerializeField] private PlayerControlls player;
    [SerializeField] private Image overlayImage; // optional; auto-created if null

    [Header("Enable Threshold")]
    [SerializeField] private int threshold = 30;

    [Header("Circle Radius (normalized 0..1)")]
    [Tooltip("Radius used when sanity <= threshold")]
    [SerializeField] private float lowRadius = 0.18f;
    [Tooltip("Radius used when sanity > threshold")]
    [SerializeField] private float highRadius = 0.30f;

    [Header("Center Control")]
    [SerializeField] private bool followPlayer = true;
    [SerializeField] private Vector2 fixedCenter = new Vector2(0.5f, 0.5f);
    [SerializeField] private Vector2 centerOffsetNormalized = Vector2.zero;

    [Header("Effect")]
    [SerializeField] private float fadeDuration = 0.35f;
    [SerializeField] private Color darknessColor = new Color(0f, 0f, 0f, 0.92f);
    [Range(0.001f, 0.5f)] [SerializeField] private float feather = 0.15f;

    // Runtime
    private Material mat;
    private bool active;
    private float currentRadius;
    private float targetRadius;
    private float alphaMultiplier;
    private const string ShaderName = "UI/RadialCutout";

    private void Awake()
    {
        if (!Application.isPlaying) return; // edit-mode setup handled by OnEnable/OnValidate

        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);

        if (player == null) player = PlayerControlls.Instance;

        EnsureOverlay();
        PlayerControlls.OnSanityChanged += HandleSanityChanged;

        currentRadius = highRadius;
        targetRadius = highRadius;
        ApplyStaticParams();
        SyncNow();
    }

    private void OnEnable()
    {
        // Ensure material and properties exist in Edit Mode too
        EnsureOverlay();

        // Push static params so the Image previews correctly in Edit Mode
        ApplyStaticParams();

        // Make sure the Image is enabled in the Editor to preview (no fade in edit mode)
        if (!Application.isPlaying && overlayImage != null)
        {
            overlayImage.enabled = true;
            alphaMultiplier = 1f; // show overlay preview
            // Set a neutral center for preview
            Vector2 center = fixedCenter + centerOffsetNormalized;
            center.x = Mathf.Clamp01(center.x);
            center.y = Mathf.Clamp01(center.y);
            if (mat != null)
            {
                mat.SetVector("_Center", new Vector4(center.x, center.y, 0f, 0f));
                mat.SetFloat("_Radius", highRadius);
                mat.SetFloat("_Feather", feather);
                mat.SetColor("_DarkColor", darknessColor);
            }
        }
    }

    private void OnDestroy()
    {
        if (Application.isPlaying)
        {
            if (_instance == this) _instance = null;
            PlayerControlls.OnSanityChanged -= HandleSanityChanged;
        }
    }

    private void EnsureOverlay()
    {
        // If a UI Image is assigned, ensure it has the correct material in both Edit and Play
        if (overlayImage != null)
        {
            var shader = Shader.Find(ShaderName);
            if (shader != null)
            {
                if (overlayImage.material == null || overlayImage.material.shader != shader)
                {
                    mat = new Material(shader);
                    overlayImage.material = mat;
                    overlayImage.material.SetTextureScale("_MainTex", new Vector2(1.75f, 1.00f));
                    overlayImage.material.SetTextureOffset("_MainTex", Vector2.zero); // optional
                }
                else
                {
                    mat = overlayImage.material;
                }
            }
            return;
        }

        // In Play Mode, auto-create a persistent overlay if none is provided
        if (!Application.isPlaying) return;

        var canvasGO = new GameObject("SanityVisionCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();
        DontDestroyOnLoad(canvasGO);

        var imgGO = new GameObject("SanityVisionOverlay");
        imgGO.transform.SetParent(canvasGO.transform, false);
        overlayImage = imgGO.AddComponent<Image>();
        overlayImage.raycastTarget = false;
        overlayImage.color = Color.white;

        var rt = overlayImage.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var shader2 = Shader.Find(ShaderName);
        if (shader2 != null)
        {
            mat = new Material(shader2);
            overlayImage.material = mat;
            overlayImage.material.SetTextureScale("_MainTex", new Vector2(1.75f, 1.00f));
            overlayImage.material.SetTextureOffset("_MainTex", Vector2.zero);
        }
        overlayImage.enabled = false;
    }

    private void HandleSanityChanged(int current, int max)
    {
        if (!Application.isPlaying) return;

        bool shouldEnable = current <= threshold;
        if (shouldEnable != active)
        {
            active = shouldEnable;
            StopAllCoroutines();
            StartCoroutine(FadeOverlay(active));
        }
    }

    private System.Collections.IEnumerator FadeOverlay(bool enabling)
    {
        float start = alphaMultiplier;
        float end = enabling ? 1f : 0f;
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            alphaMultiplier = Mathf.Lerp(start, end, t / fadeDuration);
            yield return null;
        }
        alphaMultiplier = end;
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying) return;
        if (mat == null) return;

        // Recompute target each frame so Inspector changes apply immediately
        targetRadius = active ? lowRadius : highRadius;
        currentRadius = Mathf.Lerp(currentRadius, targetRadius, Time.deltaTime * 6f);

        // Center
        Vector2 center = fixedCenter;
        if (followPlayer && player != null && Camera.main != null)
        {
            var vp = Camera.main.WorldToViewportPoint(player.transform.position);
            center.x = vp.x;
            center.y = vp.y;
        }
        center += centerOffsetNormalized;
        center.x = Mathf.Clamp01(center.x);
        center.y = Mathf.Clamp01(center.y);

        // Send to shader
        mat.SetVector("_Center", new Vector4(center.x, center.y, 0f, 0f));
        mat.SetFloat("_Radius", currentRadius);
        mat.SetFloat("_Feather", feather);
        var col = darknessColor; col.a *= alphaMultiplier;
        mat.SetColor("_DarkColor", col);

        overlayImage.enabled = alphaMultiplier > 0.01f;
    }

    private void ApplyStaticParams()
    {
        if (mat == null) return;
        mat.SetFloat("_Radius", highRadius);
        mat.SetFloat("_Feather", feather);
        mat.SetColor("_DarkColor", darknessColor);
    }

    private void SyncNow()
    {
        if (!Application.isPlaying) return;
        var p = player ?? PlayerControlls.Instance;
        if (p != null) HandleSanityChanged(p.CurrentSanity, p.MaxSanity);
    }
}
