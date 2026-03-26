using System;
using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// Attach as a child of any enemy.
//
/// On Reveal():
///  - A floating world-space arrow (↑ or ↓) appears above the enemy and bobs
///  - The enemy sprite flickers rapidly (horror feel, no colour change)
///  - An optional audio cue plays (assign upper/lower whisper clips in inspector)
///
/// Critical hit logic (read by PlayerControlls.Attack()):
///  Upper zone -> must use idle / standing attack
///  Lower zone -> must use run attack
///
/// No glow sprites needed. No colours. Purely position + motion + sound.
/// </summary>
[DisallowMultipleComponent]
public class WeakPoint : MonoBehaviour
{
    public enum WeakPointZone { Upper, Lower }

    // ── Arrow indicator ───────────────────────────────────────────
    [Header("Arrow Indicator")]
    [Tooltip("World-space TextMeshPro used to display ↑ or ↓. " +
             "Create a child GameObject, add TextMeshPro - Text (3D), assign here.")]
    [SerializeField] private TextMeshPro arrowLabel;
    [Tooltip("Local-space offset from the enemy root where the arrow appears. " +
             "X = left/right,  Y = up/down,  Z = depth. " +
             "Default (0, 1.6, 0) places the arrow above the enemy's head.")]
    [SerializeField] private Vector3 arrowOffset = new Vector3(0f, 1.6f, 0f);
    [Tooltip("How much the arrow bobs up and down (world units).")]
    [SerializeField] private float bobAmplitude = 0.12f;
    [Tooltip("Bob cycles per second.")]
    [SerializeField] private float bobSpeed = 2.2f;
    [Tooltip("Font size of the arrow label.")]
    [SerializeField] private float arrowFontSize = 4f;
    [Tooltip("Color of the arrow text. White works for any background.")]
    [SerializeField] private Color arrowColor = new Color(1f, 1f, 1f, 0.92f);

    // ── Enemy flicker ─────────────────────────────────────────────
    [Header("Enemy Flicker")]
    [Tooltip("Flicker the enemy SpriteRenderer opacity to signal vulnerability.")]
    [SerializeField] private bool enableFlicker = true;
    [Tooltip("How many opacity flashes per second.")]
    [SerializeField] private float flickerRate = 8f;
    [Tooltip("Minimum alpha during flicker (0 = fully transparent flash).")]
    [Range(0f, 1f)]
    [SerializeField] private float flickerMinAlpha = 0.35f;

    // ── Audio cues ────────────────────────────────────────────────
    [Header("Audio Cues")]
    [Tooltip("Sound played when Upper zone is revealed (e.g. a high whisper).")]
    [SerializeField] private AudioClip upperRevealSfx;
    [Tooltip("Sound played when Lower zone is revealed (e.g. a low whisper).")]
    [SerializeField] private AudioClip lowerRevealSfx;
    [Range(0f, 1f)]
    [SerializeField] private float revealSfxVolume = 0.8f;

    // ── Public state ──────────────────────────────────────────────
    public bool          IsRevealed { get; private set; }
    public WeakPointZone ActiveZone { get; private set; }

    // ── Private ───────────────────────────────────────────────────
    private Transform      parentTransform;
    private SpriteRenderer parentRenderer;
    private Color          parentOriginalColor;
    private Vector3        baseLocalPosition;
    private Vector3        baseLocalScale;

    private Coroutine bobRoutine;
    private Coroutine flickerRoutine;

    // Arrow base local position (set once in Awake from arrowHeightOffset)
    private Vector3 arrowBaseLocalPos;

    private void Awake()
    {
        parentTransform = transform.parent;

        parentRenderer = parentTransform != null
            ? parentTransform.GetComponent<SpriteRenderer>()
            : null;
        if (parentRenderer != null)
            parentOriginalColor = parentRenderer.color;

        baseLocalPosition = transform.localPosition;
        baseLocalScale    = transform.localScale;

        SetupArrow();
        SetArrowVisible(false);
    }

    private void SetupArrow()
    {
        if (arrowLabel == null) return;

        arrowLabel.fontSize  = arrowFontSize;
        arrowLabel.color     = arrowColor;
        arrowLabel.alignment = TextAlignmentOptions.Center;

        // Use the full offset vector set in the Inspector
        arrowBaseLocalPos = arrowOffset;
        arrowLabel.transform.localPosition = arrowBaseLocalPos;

        // Always face camera (billboard): zero out any inherited rotation
        arrowLabel.transform.localRotation = Quaternion.identity;
    }

    // ── Counter-flip (keeps arrow centred, not mirrored) ──────────

    private void LateUpdate()
    {
        if (parentTransform == null) return;

        float signX = Mathf.Sign(parentTransform.localScale.x);

        // Counter-flip this object's local position so the arrow stays
        // visually above the enemy regardless of which way they face
        Vector3 pos = baseLocalPosition;
        pos.x = Mathf.Abs(pos.x) * signX;
        transform.localPosition = pos;

        // Counter-flip scale so arrow text is never mirrored
        Vector3 s = baseLocalScale;
        s.x = Mathf.Abs(s.x) * signX;
        transform.localScale = s;
    }

    // ── Public API ────────────────────────────────────────────────

    /// <summary>Called by GroundingMode. Randomly picks Upper or Lower zone.</summary>
    public void Reveal()
    {
        if (IsRevealed) return;
        WeakPointZone zone = (UnityEngine.Random.value < 0.5f)
            ? WeakPointZone.Upper
            : WeakPointZone.Lower;
        RevealZone(zone);
    }

    /// <summary>Force a specific zone (useful for bosses or scripted moments).</summary>
    public void RevealZone(WeakPointZone zone)
    {
        IsRevealed = true;
        ActiveZone = zone;

        // Set arrow character
        if (arrowLabel != null)
            arrowLabel.text = (zone == WeakPointZone.Upper) ? "↑" : "↓";

        SetArrowVisible(true);

        // Play audio cue
        AudioClip cue = (zone == WeakPointZone.Upper) ? upperRevealSfx : lowerRevealSfx;
        if (cue != null)
            AudioManager.PlaySfxAt(cue, parentTransform != null
                ? parentTransform.position
                : transform.position, revealSfxVolume);

        // Start bob
        if (bobRoutine != null) StopCoroutine(bobRoutine);
        bobRoutine = StartCoroutine(BobRoutine());

        // Start flicker
        if (enableFlicker)
        {
            if (flickerRoutine != null) StopCoroutine(flickerRoutine);
            flickerRoutine = StartCoroutine(FlickerRoutine());
        }
    }

    public void Hide()
    {
        if (!IsRevealed) return;
        IsRevealed = false;

        if (bobRoutine     != null) { StopCoroutine(bobRoutine);     bobRoutine     = null; }
        if (flickerRoutine != null) { StopCoroutine(flickerRoutine); flickerRoutine = null; }

        SetArrowVisible(false);
        RestoreParentSprite();
    }

    // ── Coroutines ────────────────────────────────────────────────

    private IEnumerator BobRoutine()
    {
        while (IsRevealed)
        {
            float t = (Mathf.Sin(Time.unscaledTime * bobSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
            float yOffset = Mathf.Lerp(-bobAmplitude, bobAmplitude, t);

            if (arrowLabel != null)
                arrowLabel.transform.localPosition = arrowBaseLocalPos + new Vector3(0f, yOffset, 0f);

            yield return null;
        }

        // Restore exact offset when bob stops
        if (arrowLabel != null)
            arrowLabel.transform.localPosition = arrowBaseLocalPos;
    }

    private IEnumerator FlickerRoutine()
    {
        if (parentRenderer == null) yield break;

        float halfPeriod = 0.5f / Mathf.Max(flickerRate, 0.1f);

        while (IsRevealed)
        {
            // Dim
            Color c = parentOriginalColor;
            c.a = flickerMinAlpha;
            parentRenderer.color = c;
            yield return new WaitForSecondsRealtime(halfPeriod);

            // Restore
            parentRenderer.color = parentOriginalColor;
            yield return new WaitForSecondsRealtime(halfPeriod);
        }

        parentRenderer.color = parentOriginalColor;
    }

    // ── Helpers ───────────────────────────────────────────────────

    private void SetArrowVisible(bool visible)
    {
        if (arrowLabel != null)
            arrowLabel.gameObject.SetActive(visible);
    }

    private void RestoreParentSprite()
    {
        if (parentRenderer != null)
            parentRenderer.color = parentOriginalColor;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = IsRevealed
            ? new Color(1f, 1f, 1f, 0.8f)
            : new Color(0.4f, 0.4f, 0.4f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, 0.25f);

        // Draw arrow offset line
        if (parentTransform != null)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.5f);
            Vector3 arrowPos = parentTransform.position + arrowOffset;
            Gizmos.DrawLine(parentTransform.position, arrowPos);
            Gizmos.DrawWireSphere(arrowPos, 0.1f);
        }
    }
#endif
}
