using System.Collections;
using UnityEngine;

/// <summary>
/// Handles critical hit effects:
/// On a successful weak point hit: slows time for a few seconds then smoothly restores it.
/// Camera shake and optional SFX play on impact.
/// </summary>
[DisallowMultipleComponent]
public class CriticalHitFX : MonoBehaviour
{
    public static CriticalHitFX Instance { get; private set; }

    [Header("Critical Slow")]
    [Tooltip("Time scale applied when a weak point is hit (0.05 = almost frozen, 0.3 = slow-mo).")]
    [SerializeField] private float criticalTimeScale = 0.15f;
    [Tooltip("How long (real seconds) the slow-motion lasts after a critical hit.")]
    [SerializeField] private float criticalSlowDuration = 2.5f;
    [Tooltip("How long (real seconds) to smoothly ease back to normal time scale.")]
    [SerializeField] private float timeRestoreDuration = 0.4f;

    [Header("Camera Shake")]
    [SerializeField] private float shakeMagnitude = 0.15f;
    [SerializeField] private float shakeDuration = 0.25f;

    [Header("Audio")]
    [SerializeField] private AudioClip criticalHitSfx;
    [Range(0f, 1f)]
    [SerializeField] private float criticalHitVolume = 1f;

    private bool isActive;
    private Coroutine slowRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    public void TriggerCriticalHit(Vector3 worldPosition)
    {
        // Restart the slow even if one is already running (extends or resets the window)
        if (slowRoutine != null) StopCoroutine(slowRoutine);
        slowRoutine = StartCoroutine(CriticalSlowRoutine());

        if (CameraFollow.Instance != null)
            CameraFollow.Instance.TriggerShake(shakeMagnitude, shakeDuration);

        if (criticalHitSfx != null)
            AudioManager.PlaySfxAt(criticalHitSfx, worldPosition, criticalHitVolume);
    }

    private IEnumerator CriticalSlowRoutine()
    {
        isActive = true;

        // Snap to slow immediately
        Time.timeScale      = criticalTimeScale;
        Time.fixedDeltaTime = 0.02f * criticalTimeScale;

        // Hold for the configured real-time duration
        float elapsed = 0f;
        while (elapsed < criticalSlowDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // Smoothly ease back to normal
        float startScale = Time.timeScale;
        float restoreElapsed = 0f;
        while (restoreElapsed < timeRestoreDuration)
        {
            restoreElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(restoreElapsed / timeRestoreDuration);
            float scale = Mathf.Lerp(startScale, 1f, t);
            Time.timeScale      = scale;
            Time.fixedDeltaTime = 0.02f * scale;
            yield return null;
        }

        Time.timeScale      = 1f;
        Time.fixedDeltaTime = 0.02f;
        isActive = false;
    }

    private void OnDestroy()
    {
        if (isActive)
        {
            Time.timeScale      = 1f;
            Time.fixedDeltaTime = 0.02f;
        }
    }
}
