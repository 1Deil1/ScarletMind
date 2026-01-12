using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class CameraVignette : MonoBehaviour
{
    [Header("Vignette")]
    [Tooltip("Sprite used as the vignette overlay. Should have transparent center and darker edges.")]
    [SerializeField] private Sprite vignetteSprite;

    [Tooltip("Tint and opacity of the vignette. Alpha controls overall intensity.")]
    [SerializeField] private Color color = new Color(0f, 0f, 0f, 0.35f);

    [Tooltip("When true, shows the vignette overlay.")]
    [SerializeField] private bool enabledOnStart = true;

    [Header("Render")]
    [Tooltip("Sorting order for the overlay Canvas (higher = on top).")]
    [SerializeField] private int sortingOrder = 1000;

    [Tooltip("If true, matches the camera aspect dynamically; leave ON for most setups.")]
    [SerializeField] private bool autoMatchAspect = true;

    [Header("Layout Mode")]
    [Tooltip("Stretch the vignette to full screen. Turn OFF to use custom size/position below.")]
    [SerializeField] private bool fullScreen = true;

    [Header("Custom Size (used when Full Screen = false)")]
    [Tooltip("Choose how to interpret Width/Height values.")]
    [SerializeField] private SizeUnit sizeUnit = SizeUnit.PercentageOfScreen;
    [Tooltip("Width of the vignette overlay. If Percentage, 1.0 = full screen width.")]
    [SerializeField] [Min(0f)] private float width = 1.0f;
    [Tooltip("Height of the vignette overlay. If Percentage, 1.0 = full screen height.")]
    [SerializeField] [Min(0f)] private float height = 1.0f;
    [Tooltip("Optional uniform scale multiplier applied after size is computed.")]
    [SerializeField] private float scale = 1.0f;

    [Header("Position")]
    [Tooltip("Screen-space anchor for positioning the vignette when not full screen.")]
    [SerializeField] private Anchor presetAnchor = Anchor.Center;
    [Tooltip("Additional offset from the anchor in pixels.")]
    [SerializeField] private Vector2 pixelOffset = Vector2.zero;
    [Tooltip("Additional offset from the anchor as percentage of screen (0..1).")]
    [SerializeField] private Vector2 percentOffset = Vector2.zero;

    [Header("Padding (used when Full Screen = true)")]
    [Tooltip("Extra padding inside the fullscreen stretch (pixels). Positive shrinks inward.")]
    [SerializeField] private Vector2 innerPaddingPixels = Vector2.zero;
    [Tooltip("Extra padding inside the fullscreen stretch (% of screen). Positive shrinks inward.")]
    [SerializeField] private Vector2 innerPaddingPercent = Vector2.zero;

    private Canvas overlayCanvas;
    private Image overlayImage;

    public enum SizeUnit
    {
        Pixels,
        PercentageOfScreen
    }

    public enum Anchor
    {
        TopLeft,
        TopCenter,
        TopRight,
        CenterLeft,
        Center,
        CenterRight,
        BottomLeft,
        BottomCenter,
        BottomRight
    }

    private void Awake()
    {
        var cam = GetComponent<Camera>();
        if (cam == null)
        {
            Debug.LogWarning("CameraVignette must be placed on a Camera.");
            enabled = false;
            return;
        }

        // Create overlay canvas (ScreenSpaceCamera so it renders with this camera)
        var canvasGO = new GameObject("VignetteCanvas");
        canvasGO.transform.SetParent(transform, false);
        overlayCanvas = canvasGO.AddComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        overlayCanvas.worldCamera = cam;
        overlayCanvas.sortingOrder = sortingOrder;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>().enabled = false; // no input

        // Fullscreen image
        var imageGO = new GameObject("VignetteImage");
        imageGO.transform.SetParent(canvasGO.transform, false);
        overlayImage = imageGO.AddComponent<Image>();
        overlayImage.sprite = vignetteSprite;
        overlayImage.color = color;
        overlayImage.raycastTarget = false;
        overlayImage.preserveAspect = true; // keeps sprite aspect
        overlayImage.type = Image.Type.Simple;

        ApplyLayout(cam);

        SetEnabled(enabledOnStart);
    }

    private void LateUpdate()
    {
        if (!autoMatchAspect || overlayCanvas == null) return;
        var cam = overlayCanvas.worldCamera;
        if (cam != null) ApplyLayout(cam);
    }

    private void ApplyLayout(Camera cam)
    {
        if (overlayImage == null) return;

        var rt = overlayImage.rectTransform;

        if (fullScreen)
        {
            // Stretch to full screen with optional inner padding
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;

            // Compute padding in pixels (combine pixel + percent)
            Vector2 screenSize = GetScreenSize();
            Vector2 padPx = innerPaddingPixels + new Vector2(innerPaddingPercent.x * screenSize.x, innerPaddingPercent.y * screenSize.y);
            rt.offsetMin = new Vector2(padPx.x, padPx.y);
            rt.offsetMax = new Vector2(-padPx.x, -padPx.y);

            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
        }
        else
        {
            // Use fixed size and anchor position
            rt.anchorMin = rt.anchorMax = Vector2.zero; // we position via anchoredPosition
            rt.pivot = AnchorToPivot(presetAnchor);

            Vector2 screenSize = GetScreenSize();
            Vector2 desiredSize;

            if (sizeUnit == SizeUnit.Pixels)
            {
                desiredSize = new Vector2(width, height);
            }
            else
            {
                desiredSize = new Vector2(width * screenSize.x, height * screenSize.y);
            }

            desiredSize *= Mathf.Max(0.0001f, scale);
            rt.sizeDelta = desiredSize;

            // Compute anchored position from selected anchor plus offsets
            Vector2 basePos = AnchorToScreenPoint(presetAnchor, screenSize);
            Vector2 finalOffset = pixelOffset + new Vector2(percentOffset.x * screenSize.x, percentOffset.y * screenSize.y);

            // Since anchorMin/Max are zero, anchoredPosition is absolute in canvas space
            rt.anchoredPosition = basePos + finalOffset;

            rt.localScale = Vector3.one;
            // Optional: if you want extra bleed beyond the edges, you can add a margin via sizeDelta here
        }

        // keep sprite/color up to date
        overlayImage.sprite = vignetteSprite;
        overlayImage.color = color;
    }

    private Vector2 GetScreenSize()
    {
        // CanvasScaler handles scaling to reference resolution; for simple overlay,
        // use actual pixel size of the current screen.
        return new Vector2(Screen.width, Screen.height);
    }

    private Vector2 AnchorToScreenPoint(Anchor a, Vector2 screenSize)
    {
        switch (a)
        {
            case Anchor.TopLeft:      return new Vector2(0f, screenSize.y);
            case Anchor.TopCenter:    return new Vector2(screenSize.x * 0.5f, screenSize.y);
            case Anchor.TopRight:     return new Vector2(screenSize.x, screenSize.y);
            case Anchor.CenterLeft:   return new Vector2(0f, screenSize.y * 0.5f);
            case Anchor.Center:       return new Vector2(screenSize.x * 0.5f, screenSize.y * 0.5f);
            case Anchor.CenterRight:  return new Vector2(screenSize.x, screenSize.y * 0.5f);
            case Anchor.BottomLeft:   return new Vector2(0f, 0f);
            case Anchor.BottomCenter: return new Vector2(screenSize.x * 0.5f, 0f);
            case Anchor.BottomRight:  return new Vector2(screenSize.x, 0f);
            default:                  return new Vector2(screenSize.x * 0.5f, screenSize.y * 0.5f);
        }
    }

    private Vector2 AnchorToPivot(Anchor a)
    {
        switch (a)
        {
            case Anchor.TopLeft:      return new Vector2(0f, 1f);
            case Anchor.TopCenter:    return new Vector2(0.5f, 1f);
            case Anchor.TopRight:     return new Vector2(1f, 1f);
            case Anchor.CenterLeft:   return new Vector2(0f, 0.5f);
            case Anchor.Center:       return new Vector2(0.5f, 0.5f);
            case Anchor.CenterRight:  return new Vector2(1f, 0.5f);
            case Anchor.BottomLeft:   return new Vector2(0f, 0f);
            case Anchor.BottomCenter: return new Vector2(0.5f, 0f);
            case Anchor.BottomRight:  return new Vector2(1f, 0f);
            default:                  return new Vector2(0.5f, 0.5f);
        }
    }

    // Public API
    public void SetEnabled(bool enabledOverlay)
    {
        if (overlayCanvas != null)
            overlayCanvas.enabled = enabledOverlay;
    }

    public void SetSprite(Sprite s)
    {
        vignetteSprite = s;
        if (overlayImage != null)
            overlayImage.sprite = s;
    }

    public void SetColor(Color c)
    {
        color = c;
        if (overlayImage != null)
            overlayImage.color = c;
    }

    public void SetSortingOrder(int order)
    {
        sortingOrder = order;
        if (overlayCanvas != null)
            overlayCanvas.sortingOrder = order;
    }

    public void SetFullScreen(bool isFull)
    {
        fullScreen = isFull;
        var cam = overlayCanvas != null ? overlayCanvas.worldCamera : Camera.main;
        if (cam != null) ApplyLayout(cam);
    }

    public void SetSize(float w, float h, SizeUnit unit = SizeUnit.PercentageOfScreen)
    {
        width = Mathf.Max(0f, w);
        height = Mathf.Max(0f, h);
        sizeUnit = unit;
        var cam = overlayCanvas != null ? overlayCanvas.worldCamera : Camera.main;
        if (cam != null) ApplyLayout(cam);
    }

    public void SetScale(float s)
    {
        scale = Mathf.Max(0f, s);
        var cam = overlayCanvas != null ? overlayCanvas.worldCamera : Camera.main;
        if (cam != null) ApplyLayout(cam);
    }

    public void SetAnchor(Anchor a)
    {
        presetAnchor = a;
        var cam = overlayCanvas != null ? overlayCanvas.worldCamera : Camera.main;
        if (cam != null) ApplyLayout(cam);
    }

    public void SetOffsets(Vector2 pixels, Vector2 percent)
    {
        pixelOffset = pixels;
        percentOffset = percent;
        var cam = overlayCanvas != null ? overlayCanvas.worldCamera : Camera.main;
        if (cam != null) ApplyLayout(cam);
    }

    public void SetInnerPadding(Vector2 pixels, Vector2 percent)
    {
        innerPaddingPixels = pixels;
        innerPaddingPercent = percent;
        var cam = overlayCanvas != null ? overlayCanvas.worldCamera : Camera.main;
        if (cam != null) ApplyLayout(cam);
    }
}
