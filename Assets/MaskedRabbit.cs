using System;
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(Collider2D))]
public class MaskedRabbit : MonoBehaviour
{
    /*
    Unity Editor setup:
    - Assign the SmallRabbit prefab to smallRabbitPrefab.
    - Create Animator bool parameters named isIdle, isSpawning, and isTeleporting.
    - Add Animation Events named SpawnSmallRabbit on the attack clip, ExecuteTeleport on the teleport clip used for normal teleports, and ExecuteDeath on the teleport clip used for the death/disappear sequence.
    - Put valid platform colliders on layers included by groundLayer so teleport ground snapping can find a landing spot.
    - Keep the main damageable Collider2D on this same GameObject so PlayerControlls.SendMessage("TakeDamage", ...) reaches this script.
    - Assign facingSpriteRenderer if the visible sprite is on a child object or uses SpriteRenderer.flipX.
    - If the rabbit still looks backwards when turning toward the player, enable invertFacingDirection in the Inspector.
    */

    private enum State
    {
        Idle,
        Spawning,
        WaitingForHit,
        Teleporting,
        Dying
    }

    private const string IdleParameter = "isIdle";
    private const string SpawningParameter = "isSpawning";
    private const string TeleportingParameter = "isTeleporting";
    private const string SpawnClipName = "attack one-shot";
    private const string TeleportClipName = "teleport one-shot";
    private const int MaxHits = 4;

    [Header("References")]
    public GameObject smallRabbitPrefab;
    public float detectionRange = 10f;
    public float teleportDistance = 4f;
    public float playerKnockbackForce = 8f;
    public Transform playerTransform;
    public Animator animator;

    [Header("Facing")]
    [SerializeField] private SpriteRenderer facingSpriteRenderer;
    [SerializeField] private Transform facingTransform;
    [SerializeField] private bool useSpriteFlip = true;
    [SerializeField] private bool invertFacingDirection;

    [Header("Spawn Tuning")]
    [SerializeField] private float spawnForwardDistance = 1.25f;
    [SerializeField] private float spawnOffsetX = 0.75f;
    [SerializeField] private float spawnOffsetY = 0.35f;

    [Header("Teleport Ground Snap")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float groundProbeStartHeight = 2f;
    [SerializeField] private float groundProbeDistance = 8f;
    [SerializeField] private float groundSnapPadding = 0.02f;

    [Header("Animation Timing")]
    [SerializeField] private float animationEntryDelay = 0.05f;
    [SerializeField] private float animationFallbackDuration = 0.8f;
    [SerializeField] [Range(0f, 1f)] private float teleportFallbackNormalizedTime = 0.5f;

    private State currentState = State.Idle;
    private int hitCount;
    private bool spawnedThisCycle;
    private bool teleportedThisCycle;
    private bool deathExecuted;
    private int lastProcessedHitFrame = -1;
    private Coroutine stateRoutine;
    private Rigidbody2D rb;
    private Rigidbody2D playerRigidbody;
    private Collider2D primaryCollider;
    private Collider2D[] cachedColliders;
    private SpriteRenderer[] cachedSpriteRenderers;
    private Vector3 facingTransformBaseScale = Vector3.one;
    private float currentFacingDirection = 1f;

    private void Reset()
    {
        animator = GetComponent<Animator>();
        facingTransform = transform;
        facingSpriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    private void OnValidate()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (facingTransform == null)
        {
            facingTransform = transform;
        }

        if (facingSpriteRenderer == null)
        {
            facingSpriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        primaryCollider = GetComponent<Collider2D>();
        cachedColliders = GetComponentsInChildren<Collider2D>(true);
        cachedSpriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);

        if (facingTransform == null)
        {
            facingTransform = transform;
        }

        if (facingSpriteRenderer == null && cachedSpriteRenderers.Length > 0)
        {
            facingSpriteRenderer = cachedSpriteRenderers[0];
        }

        facingTransformBaseScale = facingTransform != null ? facingTransform.localScale : transform.localScale;
        currentFacingDirection = ComputeVisualFacingDirection();

        ConfigureDamageRelays();

        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    private void Start()
    {
        AcquirePlayerReferences();
        EnterState(State.Idle);
    }

    private void Update()
    {
        AcquirePlayerReferences();

        if (currentState != State.Dying)
        {
            FacePlayer();
        }

        if (currentState == State.Idle && IsPlayerWithinDetectionRange())
        {
            EnterState(State.Spawning);
        }
    }

    private void FixedUpdate()
    {
        if (rb != null)
        {
            rb.velocity = Vector2.zero;
        }
    }

    /// <summary>
    /// Called by the player attack system when the MaskedRabbit is struck.
    /// </summary>
    public void OnHit()
    {
        if (currentState != State.WaitingForHit)
        {
            return;
        }

        if (lastProcessedHitFrame == Time.frameCount)
        {
            return;
        }

        lastProcessedHitFrame = Time.frameCount;

        ApplyKnockbackToPlayer();
        hitCount++;

        if (hitCount >= MaxHits)
        {
            EnterState(State.Dying);
            return;
        }

        EnterState(State.Teleporting);
    }

    /// <summary>
    /// Compatibility hook for player attacks that still call TakeDamage through SendMessage.
    /// </summary>
    /// <param name="damageAmount">Unused damage amount supplied by the player attack.</param>
    public void TakeDamage(object damageAmount)
    {
        OnHit();
    }

    /// <summary>
    /// Animation Event for the attack clip that spawns one SmallRabbit.
    /// </summary>
    public void SpawnSmallRabbit()
    {
        TrySpawnSmallRabbit();
    }

    /// <summary>
    /// Animation Event for the teleport clip that relocates the MaskedRabbit.
    /// </summary>
    public void ExecuteTeleport()
    {
        if (currentState != State.Teleporting || teleportedThisCycle)
        {
            return;
        }

        teleportedThisCycle = true;
        Vector2 targetPosition = CalculateTeleportPosition();

        if (rb != null)
        {
            rb.position = targetPosition;
            rb.velocity = Vector2.zero;
        }

        transform.position = new Vector3(targetPosition.x, targetPosition.y, transform.position.z);
    }

    /// <summary>
    /// Animation Event for the death clip that disables collisions and hides the rabbit.
    /// </summary>
    public void ExecuteDeath()
    {
        if (currentState != State.Dying || deathExecuted)
        {
            return;
        }

        deathExecuted = true;

        if (rb != null)
        {
            rb.velocity = Vector2.zero;
            rb.simulated = false;
        }

        for (int index = 0; index < cachedColliders.Length; index++)
        {
            if (cachedColliders[index] != null)
            {
                cachedColliders[index].enabled = false;
            }
        }

        for (int index = 0; index < cachedSpriteRenderers.Length; index++)
        {
            if (cachedSpriteRenderers[index] != null)
            {
                cachedSpriteRenderers[index].enabled = false;
            }
        }
    }

    private void EnterState(State newState)
    {
        if (currentState == State.Dying && newState != State.Dying)
        {
            return;
        }

        if (stateRoutine != null)
        {
            StopCoroutine(stateRoutine);
            stateRoutine = null;
        }

        currentState = newState;

        if (currentState == State.Spawning)
        {
            spawnedThisCycle = false;
        }
        else if (currentState == State.Teleporting)
        {
            teleportedThisCycle = false;
        }
        else if (currentState == State.Dying)
        {
            deathExecuted = false;
        }

        ApplyAnimatorParameters();

        if (currentState == State.Spawning)
        {
            stateRoutine = StartCoroutine(FinishAfterAnimation(State.Spawning, SpawnClipName, State.WaitingForHit));
        }
        else if (currentState == State.Teleporting)
        {
            stateRoutine = StartCoroutine(FinishAfterAnimation(State.Teleporting, TeleportClipName, State.Idle));
        }
        else if (currentState == State.Dying)
        {
            stateRoutine = StartCoroutine(DestroyAfterAnimation());
        }
    }

    private IEnumerator FinishAfterAnimation(State expectedState, string clipName, State nextState)
    {
        float totalWait = GetClipWaitTime(clipName);

        if (expectedState == State.Teleporting)
        {
            float fallbackDelay = Mathf.Clamp(totalWait * teleportFallbackNormalizedTime, 0f, totalWait);
            if (fallbackDelay > 0f)
            {
                yield return new WaitForSeconds(fallbackDelay);
            }

            if (currentState != expectedState)
            {
                yield break;
            }

            if (!teleportedThisCycle)
            {
                ExecuteTeleport();
            }

            float remainingDelay = Mathf.Max(0f, totalWait - fallbackDelay);
            if (remainingDelay > 0f)
            {
                yield return new WaitForSeconds(remainingDelay);
            }
        }
        else
        {
            yield return new WaitForSeconds(totalWait);
        }

        if (currentState != expectedState)
        {
            yield break;
        }

        if (expectedState == State.Spawning && !spawnedThisCycle)
        {
            TrySpawnSmallRabbit();
        }

        stateRoutine = null;
        EnterState(nextState);
    }

    private IEnumerator DestroyAfterAnimation()
    {
        yield return new WaitForSeconds(GetClipWaitTime(TeleportClipName));

        if (currentState != State.Dying)
        {
            yield break;
        }

        stateRoutine = null;
        Destroy(gameObject);
    }

    private void ApplyAnimatorParameters()
    {
        if (animator == null)
        {
            return;
        }

        bool isIdle = currentState == State.Idle || currentState == State.WaitingForHit;
        bool isSpawning = currentState == State.Spawning;
        bool isTeleporting = currentState == State.Teleporting || currentState == State.Dying;

        animator.SetBool(IdleParameter, isIdle);
        animator.SetBool(SpawningParameter, isSpawning);
        animator.SetBool(TeleportingParameter, isTeleporting);
    }

    private void AcquirePlayerReferences()
    {
        if (playerTransform == null)
        {
            if (PlayerControlls.Instance != null)
            {
                playerTransform = PlayerControlls.Instance.transform;
            }
            else
            {
                GameObject taggedPlayer = GameObject.FindWithTag("Player");
                if (taggedPlayer != null)
                {
                    playerTransform = taggedPlayer.transform;
                }
            }
        }

        if (playerTransform != null && playerRigidbody == null)
        {
            playerRigidbody = playerTransform.GetComponent<Rigidbody2D>();
        }
    }

    private bool IsPlayerWithinDetectionRange()
    {
        if (playerTransform == null)
        {
            return false;
        }

        return Vector2.Distance(transform.position, playerTransform.position) <= detectionRange;
    }

    private void ApplyKnockbackToPlayer()
    {
        if (playerRigidbody == null)
        {
            return;
        }

        float direction = playerTransform.position.x >= transform.position.x ? 1f : -1f;
        playerRigidbody.AddForce(Vector2.right * direction * playerKnockbackForce, ForceMode2D.Impulse);
    }

    private Vector2 CalculateTeleportPosition()
    {
        Vector2 currentPosition = rb != null ? rb.position : (Vector2)transform.position;
        if (playerTransform == null)
        {
            return currentPosition;
        }

        float playerFacingDirection = playerTransform.localScale.x >= 0f ? 1f : -1f;
        float sideFromPlayer = -playerFacingDirection;
        if (Mathf.Approximately(playerFacingDirection, 0f))
        {
            sideFromPlayer = playerTransform.position.x >= transform.position.x ? -1f : 1f;
        }

        Vector2 targetPosition = new Vector2(playerTransform.position.x + (sideFromPlayer * teleportDistance), currentPosition.y);

        if (Mathf.Abs(targetPosition.x - currentPosition.x) < 0.1f)
        {
            sideFromPlayer *= -1f;
            targetPosition.x = playerTransform.position.x + (sideFromPlayer * teleportDistance);
        }

        if (groundLayer.value == 0)
        {
            return targetPosition;
        }

        Vector2 rayOrigin = targetPosition + Vector2.up * groundProbeStartHeight;
        float rayDistance = groundProbeStartHeight + groundProbeDistance;
        RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.down, rayDistance, groundLayer);
        if (hit.collider != null)
        {
            targetPosition.y = hit.point.y + GetFeetOffsetFromPivot();
        }

        return targetPosition;
    }

    private float GetFeetOffsetFromPivot()
    {
        if (primaryCollider == null)
        {
            return 0f;
        }

        return transform.position.y - primaryCollider.bounds.min.y + groundSnapPadding;
    }

    private float GetClipWaitTime(string clipName)
    {
        float clipLength = GetClipLength(clipName);
        if (clipLength <= 0f)
        {
            return animationFallbackDuration;
        }

        return animationEntryDelay + clipLength;
    }

    private float GetClipLength(string clipName)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            return 0f;
        }

        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
        for (int index = 0; index < clips.Length; index++)
        {
            AnimationClip clip = clips[index];
            if (clip != null && string.Equals(clip.name, clipName, StringComparison.OrdinalIgnoreCase))
            {
                return clip.length;
            }
        }

        return 0f;
    }

    private bool TrySpawnSmallRabbit()
    {
        if (currentState != State.Spawning || spawnedThisCycle || smallRabbitPrefab == null)
        {
            return false;
        }

        spawnedThisCycle = true;

        Vector3 spawnPosition = CalculateSpawnPosition();
        GameObject spawnedRabbit = Instantiate(smallRabbitPrefab, spawnPosition, Quaternion.identity);
        SmallRabbit smallRabbit = spawnedRabbit.GetComponentInChildren<SmallRabbit>(true);
        if (smallRabbit == null)
        {
            Debug.LogWarning($"MaskedRabbit '{name}' spawned prefab '{smallRabbitPrefab.name}', but no SmallRabbit component was found on the prefab root or its children.");
            return true;
        }

        if (smallRabbit.playerTransform == null && playerTransform != null)
        {
            smallRabbit.playerTransform = playerTransform;
        }

        smallRabbit.ActivateSpawned();
        return true;
    }

    private Vector3 CalculateSpawnPosition()
    {
        float facingDirection = GetFacingDirection();
        float forwardDistance = Mathf.Max(0.1f, Mathf.Max(Mathf.Abs(spawnOffsetX), spawnForwardDistance));
        float verticalOffset = Mathf.Max(0f, spawnOffsetY);

        return transform.position + new Vector3(facingDirection * forwardDistance, verticalOffset, 0f);
    }

    private float GetFacingDirection()
    {
        if (Mathf.Abs(currentFacingDirection) > 0.001f)
        {
            return Mathf.Sign(currentFacingDirection);
        }

        if (playerTransform != null)
        {
            return playerTransform.position.x >= transform.position.x ? 1f : -1f;
        }

        return 1f;
    }

    private float ComputeVisualFacingDirection()
    {
        if (useSpriteFlip && facingSpriteRenderer != null)
        {
            return facingSpriteRenderer.flipX ? -1f : 1f;
        }

        Transform targetTransform = facingTransform != null ? facingTransform : transform;
        float visualDirection = Mathf.Abs(targetTransform.localScale.x) > 0.001f
            ? Mathf.Sign(targetTransform.localScale.x)
            : 1f;

        return visualDirection;
    }

    private void FacePlayer()
    {
        if (playerTransform == null)
        {
            return;
        }

        float direction = playerTransform.position.x >= transform.position.x ? 1f : -1f;
        FaceDirection(direction);
    }

    private void FaceDirection(float direction)
    {
        if (Mathf.Abs(direction) <= 0.001f)
        {
            return;
        }

        float desiredDirection = invertFacingDirection ? -Mathf.Sign(direction) : Mathf.Sign(direction);

        if (useSpriteFlip && facingSpriteRenderer != null)
        {
            facingSpriteRenderer.flipX = desiredDirection < 0f;
        }

        Transform targetTransform = facingTransform != null ? facingTransform : transform;
        Vector3 baseScale = facingTransform != null ? facingTransformBaseScale : transform.localScale;
        Vector3 localScale = targetTransform.localScale;

        if (useSpriteFlip && facingSpriteRenderer != null)
        {
            localScale.x = Mathf.Abs(baseScale.x);
        }
        else
        {
            localScale.x = Mathf.Abs(baseScale.x) * desiredDirection;
        }

        localScale.y = baseScale.y;
        localScale.z = baseScale.z;
        targetTransform.localScale = localScale;
        currentFacingDirection = ComputeVisualFacingDirection();
    }

    private void ConfigureDamageRelays()
    {
        for (int index = 0; index < cachedColliders.Length; index++)
        {
            Collider2D collider = cachedColliders[index];
            if (collider == null || collider.gameObject == gameObject)
            {
                continue;
            }

            MaskedRabbitDamageRelay relay = collider.GetComponent<MaskedRabbitDamageRelay>();
            if (relay == null)
            {
                relay = collider.gameObject.AddComponent<MaskedRabbitDamageRelay>();
            }

            relay.Bind(this);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        if (playerTransform != null)
        {
            Gizmos.color = Color.cyan;
            Vector3 leftTarget = new Vector3(playerTransform.position.x - teleportDistance, transform.position.y, transform.position.z);
            Vector3 rightTarget = new Vector3(playerTransform.position.x + teleportDistance, transform.position.y, transform.position.z);
            Gizmos.DrawWireSphere(leftTarget, 0.2f);
            Gizmos.DrawWireSphere(rightTarget, 0.2f);
        }
    }
}

internal sealed class MaskedRabbitDamageRelay : MonoBehaviour
{
    private MaskedRabbit owner;

    public void Bind(MaskedRabbit maskedRabbit)
    {
        owner = maskedRabbit;
    }

    public void OnHit()
    {
        owner?.OnHit();
    }

    public void TakeDamage(object damageAmount)
    {
        owner?.TakeDamage(damageAmount);
    }
}