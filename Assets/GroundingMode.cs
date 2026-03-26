using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Attach to the player root (same GameObject as PlayerControlls).
/// Grounding Mode: slow time -> anchoring anim -> restore time -> reveal weak points.
/// Player can move freely throughout. No dash effect on exit.
/// All timers use unscaledDeltaTime so UI and cooldowns are never affected by timeScale.
/// </summary>
[DisallowMultipleComponent]
public class GroundingMode : MonoBehaviour
{
    public static GroundingMode Instance { get; private set; }

    [Header("Input")]
    [SerializeField] private KeyCode activationKey = KeyCode.Q;

    [Header("Cooldown")]
    [Tooltip("Real-time seconds before Grounding Mode can be reused.")]
    [SerializeField] private float cooldown = 22f;

    [Header("Time Slowdown")]
    [SerializeField] private float slowTimeScale = 0.3f;
    [Tooltip("How long (real seconds) the slow-motion phase lasts before weak points are revealed.")]
    [SerializeField] private float slowDuration = 3f;
    [Tooltip("Seconds to smoothly ease back to normal time scale after slow ends.")]
    [SerializeField] private float timeRestoreDuration = 0.4f;

    [Header("Weak Point Window")]
    [Tooltip("How long (real seconds) weak points remain visible and hittable.")]
    [SerializeField] private float weakPointDuration = 4f;
    [Tooltip("Radius around the player to search for enemies to reveal weak points on.")]
    [SerializeField] private float weakPointRadius = 14f;
    [SerializeField] private LayerMask enemyLayer;

    [Header("Anchoring Animation")]
    [Tooltip("Trigger on the player Animator played when Grounding Mode activates. Leave empty to skip.")]
    [SerializeField] private string anchorTrigger = "grounding";

    [Header("Camera Shake")]
    [SerializeField] private float activationShakeMagnitude = 0.08f;
    [SerializeField] private float activationShakeDuration  = 0.25f;

    // ?? Public state ??????????????????????????????????????????????
    public bool  IsActive       { get; private set; }
    public bool  WeakPointsOpen { get; private set; }
    public float Cooldown       => cooldown;

    // ?? Events ????????????????????????????????????????????????????
    public static event Action OnGroundingActivated;
    public static event Action OnWeakPointsRevealed;
    public static event Action OnWeakPointsExpired;

    // ?? Private ???????????????????????????????????????????????????
    private float lastActivationRealTime = -999f;
    private Coroutine activeRoutine;
    private Animator playerAnimator;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        playerAnimator = GetComponent<Animator>();
    }

    private void Update()
    {
        if (PlayerControlls.Instance != null && Input.GetKeyDown(activationKey))
            TryActivate();
    }

    // ?? Public API ????????????????????????????????????????????????

    public bool CanActivate() =>
        !IsActive && (Time.unscaledTime - lastActivationRealTime) >= cooldown;

    public float CooldownRemaining() =>
        Mathf.Max(0f, cooldown - (Time.unscaledTime - lastActivationRealTime));

    public void TryActivate()
    {
        if (!CanActivate()) return;
        if (activeRoutine != null) StopCoroutine(activeRoutine);
        activeRoutine = StartCoroutine(GroundingRoutine());
    }

    // ?? Core routine ??????????????????????????????????????????????

    private IEnumerator GroundingRoutine()
    {
        IsActive = true;
        lastActivationRealTime = Time.unscaledTime;

        // ?? Anchoring animation + camera shake ?????????????????
        // Player is NOT locked - they can still move freely
        if (playerAnimator != null && !string.IsNullOrEmpty(anchorTrigger))
            playerAnimator.SetTrigger(anchorTrigger);

        if (CameraFollow.Instance != null)
            CameraFollow.Instance.TriggerShake(activationShakeMagnitude, activationShakeDuration);

        OnGroundingActivated?.Invoke();

        // ?? Slow time ??????????????????????????????????????????
        Time.timeScale      = slowTimeScale;
        Time.fixedDeltaTime = 0.02f * slowTimeScale;

        float elapsed = 0f;
        while (elapsed < slowDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // ?? Restore time smoothly ??????????????????????????????
        yield return StartCoroutine(RestoreTimeScale(timeRestoreDuration));

        // ?? Reveal weak points ?????????????????????????????????
        WeakPointsOpen = true;
        RevealNearbyWeakPoints();
        OnWeakPointsRevealed?.Invoke();

        // ?? Weak point window ??????????????????????????????????
        float window = 0f;
        while (window < weakPointDuration)
        {
            window += Time.unscaledDeltaTime;
            yield return null;
        }

        // ?? Hide weak points and finish ????????????????????????
        HideAllWeakPoints();
        WeakPointsOpen = false;
        IsActive = false;

        OnWeakPointsExpired?.Invoke();
    }

    private IEnumerator RestoreTimeScale(float duration)
    {
        float startScale = Time.timeScale;
        float elapsed    = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float scale = Mathf.Lerp(startScale, 1f, t);
            Time.timeScale      = scale;
            Time.fixedDeltaTime = 0.02f * scale;
            yield return null;
        }
        Time.timeScale      = 1f;
        Time.fixedDeltaTime = 0.02f;
    }

    // ?? Weak point helpers ????????????????????????????????????????

    private void RevealNearbyWeakPoints()
    {
        Collider2D[] hits = enemyLayer.value != 0
            ? Physics2D.OverlapCircleAll(transform.position, weakPointRadius, enemyLayer)
            : Physics2D.OverlapCircleAll(transform.position, weakPointRadius);

        foreach (var h in hits)
        {
            if (h == null) continue;
            var wp = h.transform.root.GetComponentInChildren<WeakPoint>(includeInactive: true);
            if (wp != null) wp.Reveal();
        }
    }

    private void HideAllWeakPoints()
    {
        Collider2D[] hits = enemyLayer.value != 0
            ? Physics2D.OverlapCircleAll(transform.position, weakPointRadius * 2f, enemyLayer)
            : Physics2D.OverlapCircleAll(transform.position, weakPointRadius * 2f);

        foreach (var h in hits)
        {
            if (h == null) continue;
            var wp = h.transform.root.GetComponentInChildren<WeakPoint>(includeInactive: true);
            if (wp != null) wp.Hide();
        }
    }

    private void OnDestroy()
    {
        if (IsActive)
        {
            Time.timeScale      = 1f;
            Time.fixedDeltaTime = 0.02f;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, weakPointRadius);
    }
#endif
}
