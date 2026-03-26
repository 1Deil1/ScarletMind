using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Attach to any persistent Canvas GameObject.
/// Shows a radial/fill-based cooldown indicator for Grounding Mode.
/// Assign the Image (set Image Type = Filled, Fill Method = Radial360) in the Inspector.
/// Optionally assign a TextMeshProUGUI label to show "READY" or the seconds remaining.
/// </summary>
[DisallowMultipleComponent]
public class GroundingModeUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Image with Image Type = Filled, Fill Method = Radial360.")]
    [SerializeField] private Image cooldownRadial;
    [Tooltip("Optional label that shows READY or remaining seconds.")]
    [SerializeField] private TextMeshProUGUI statusLabel;

    [Header("Colors")]
    [SerializeField] private Color readyColor    = new Color(0.9f, 0.9f, 0.9f, 1f);
    [SerializeField] private Color cooldownColor = new Color(0.4f, 0.4f, 0.4f, 0.7f);
    [SerializeField] private Color activeColor   = new Color(1f,   0.9f, 0.3f, 1f);

    [Header("Label Text")]
    [SerializeField] private string readyText    = "READY";
    [SerializeField] private string activeText   = "ACTIVE";
    [SerializeField] private bool   showSeconds  = true;

    private void Update()
    {
        if (GroundingMode.Instance == null)
        {
            SetRadial(1f, cooldownColor);
            SetLabel("");
            return;
        }

        if (GroundingMode.Instance.IsActive)
        {
            SetRadial(1f, activeColor);
            SetLabel(activeText);
            return;
        }

        float remaining = GroundingMode.Instance.CooldownRemaining();
        float total     = GroundingMode.Instance.Cooldown;

        if (remaining <= 0f)
        {
            SetRadial(1f, readyColor);
            SetLabel(readyText);
        }
        else
        {
            float fill = 1f - Mathf.Clamp01(remaining / Mathf.Max(total, 0.001f));
            SetRadial(fill, cooldownColor);
            SetLabel(showSeconds ? Mathf.CeilToInt(remaining).ToString() : "");
        }
    }

    private void SetRadial(float fill, Color color)
    {
        if (cooldownRadial == null) return;
        cooldownRadial.fillAmount = fill;
        cooldownRadial.color      = color;
    }

    private void SetLabel(string text)
    {
        if (statusLabel == null) return;
        statusLabel.text = text;
    }
}
