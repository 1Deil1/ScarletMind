using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(Collider2D))]
public class SmallRabbit : MonoBehaviour
{
    /*
    Unity Editor setup:
    - Create Animator bool parameters named isHopping, isAttacking, and isDead.
    - Add an Animation Event named DealDamage on the attack clip.
    - Put valid platform colliders on layers included by groundLayer so the grounded hop check works.
    - Keep the main damageable Collider2D on this same GameObject so PlayerControlls.SendMessage("TakeDamage", ...) reaches this script.
    - Ensure the player GameObject uses tag Player and has a Rigidbody2D plus either a TakeDamage(int) receiver or the existing PlayerControlls component.
    - Assign facingSpriteRenderer if the visible sprite is on a child object or uses SpriteRenderer.flipX.
    - If the rabbit still looks backwards when moving toward the player, enable invertFacingDirection in the Inspector.
    - The Teleport-death clip is used for spawn intro only; SmallRabbit death is currently instant and does not use a death animation.
    */

    private enum State
    {
        Hopping,
        Attacking,
        Dead
    }

    private const string HoppingParameter = "isHopping";
    private const string AttackingParameter = "isAttacking";
    private const string DeadParameter = "isDead";
    private const string AttackClipName = "attack one-shot";
    private const string DeathClipName = "Teleport-death";

    [Header("References")]
    public Transform playerTransform;
    public float hopForceX = 5f;
    public float hopForceY = 6f;
    public float hopInterval = 0.8f;
    public float attackRange = 1.2f;
    public int damage = 1;
    public LayerMask groundLayer;
    public Animator animator;
    public Rigidbody2D rb;

    [Header("Facing")]
    [SerializeField] private SpriteRenderer facingSpriteRenderer;
    [SerializeField] private Transform facingTransform;
    [SerializeField] private bool useSpriteFlip = true;
    [SerializeField] private bool invertFacingDirection;

    [Header("Spawn Activation")]
    [SerializeField] private bool startDormantUntilSpawned = true;

    [Header("Spawn Intro")]
    [SerializeField] private bool playSpawnIntroAnimation = true;
    [SerializeField] private string spawnIntroStateName = "Teleport-death";

    [Header("Physics")]
    [SerializeField] private float minimumGravityScale = 3f;

    [Header("Ground Check")]
    [SerializeField] private float groundCheckRadius = 0.12f;
    [SerializeField] private float groundCheckOffset = 0.05f;

    [Header("Animation Timing")]
    [SerializeField] private float animationEntryDelay = 0.05f;
    [SerializeField] private float animationFallbackDuration = 0.5f;

    [Header("Death")]
    [SerializeField] private float destroyDelayOnHit;

    [Header("Health")]
    [SerializeField] private int maxHealth = 1;

    [Header("Audio - Enemy SFX")]
    [SerializeField] private AudioClip attackHitSfx;
    [SerializeField] private AudioClip attackMissSfx;
    [Tooltip("Sound played when the rabbit starts a hop.")]
    [SerializeField] private AudioClip hopSfx;
    [SerializeField] private bool playAttackHitSfx = true;
    [SerializeField] private bool playAttackMissSfx = true;
    [SerializeField] private bool playHopSfx = true;
    [Range(0f, 1f)] [SerializeField] private float attackHitSfxVolume = 1f;
    [Range(0f, 1f)] [SerializeField] private float attackMissSfxVolume = 1f;
    [Range(0f, 1f)] [SerializeField] private float hopSfxVolume = 1f;

    private State currentState = State.Hopping;
    private float nextHopTime;
    private bool damageDealtThisAttack;
    private bool deathExecuted;
    private Coroutine stateRoutine;
    private Collider2D primaryCollider;
    private Collider2D[] cachedColliders;
    private SpriteRenderer[] cachedSpriteRenderers;
    private bool spawnActive;
    private bool hasStarted;
    private bool pendingSpawnIntro;
    private bool isSpawnIntroPlaying;
    private Vector3 facingTransformBaseScale = Vector3.one;
    private int currentHealth;

    private void Reset()
    {
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        facingTransform = transform;
        facingSpriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    private void OnValidate()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
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
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
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
        ConfigureDamageRelays();

        currentHealth = Mathf.Max(1, maxHealth);

        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.gravityScale = Mathf.Max(rb.gravityScale, minimumGravityScale);

        spawnActive = !startDormantUntilSpawned;
        ApplySpawnActiveState();
    }

    private void Start()
    {
        hasStarted = true;

        if (!spawnActive)
        {
            return;
        }

        AcquirePlayerIfNeeded();

        if (pendingSpawnIntro && playSpawnIntroAnimation)
        {
            pendingSpawnIntro = false;
            BeginSpawnIntro();
            return;
        }

        nextHopTime = Time.time + hopInterval;
        EnterState(State.Hopping);
    }

    private void Update()
    {
        if (!spawnActive)
        {
            return;
        }

        if (isSpawnIntroPlaying)
        {
            return;
        }

        AcquirePlayerIfNeeded();

        if (currentState != State.Dead)
        {
            FacePlayer();
        }

        if (currentState == State.Dead || currentState == State.Attacking)
        {
            return;
        }

        if (IsPlayerWithinAttackRange())
        {
            EnterState(State.Attacking);
            return;
        }

        if (currentState == State.Hopping && IsGrounded() && Time.time >= nextHopTime)
        {
            PerformHop();
        }
    }

    /// <summary>
    /// Activates a spawned SmallRabbit so it becomes visible, simulated, and starts hopping.
    /// </summary>
    public void ActivateSpawned()
    {
        if (spawnActive)
        {
            return;
        }

        spawnActive = true;
        ApplySpawnActiveState();
        PrepareAnimatorForSpawnIntro();

        if (!hasStarted)
        {
            pendingSpawnIntro = playSpawnIntroAnimation;
            return;
        }

        AcquirePlayerIfNeeded();

        if (playSpawnIntroAnimation)
        {
            BeginSpawnIntro();
            return;
        }

        nextHopTime = Time.time + hopInterval;
        EnterState(State.Hopping);
    }

    /// <summary>
    /// Called by the player attack system when the SmallRabbit is struck.
    /// </summary>
    public void OnHit()
    {
        ApplyDamage(1);
    }

    public void TakeDamage(object damageAmount)
    {
        int parsedDamage = 1;
        if (damageAmount is int intDamage)
        {
            parsedDamage = intDamage;
        }
        else if (damageAmount != null)
        {
            int.TryParse(damageAmount.ToString(), out parsedDamage);
            if (parsedDamage <= 0)
            {
                parsedDamage = 1;
            }
        }

        ApplyDamage(parsedDamage);
    }

    private void ApplyDamage(int damageAmount)
    {
        if (!spawnActive || currentState == State.Dead)
        {
            return;
        }

        currentHealth -= Mathf.Max(1, damageAmount);
        if (currentHealth <= 0)
        {
            DieImmediately();
        }
    }

    /// <summary>
    /// Animation Event for the attack clip that applies damage to the player.
    /// </summary>
    public void DealDamage()
    {
        if (currentState != State.Attacking || damageDealtThisAttack || !IsPlayerWithinAttackRange())
        {
            return;
        }

        damageDealtThisAttack = true;
        if (playAttackHitSfx && attackHitSfx != null)
        {
            AudioManager.PlaySfxAt(attackHitSfx, transform.position, attackHitSfxVolume);
        }
        ApplyDamageToPlayer();
    }

    /// <summary>
    /// Optional helper for future death clips; currently unused because SmallRabbit death is instant.
    /// </summary>
    public void ExecuteDeath()
    {
        if (currentState != State.Dead || deathExecuted)
        {
            return;
        }

        DisableAndHide();
    }

    private void EnterState(State newState)
    {
        if (currentState == State.Dead && newState != State.Dead)
        {
            return;
        }

        if (stateRoutine != null)
        {
            StopCoroutine(stateRoutine);
            stateRoutine = null;
        }

        currentState = newState;
        isSpawnIntroPlaying = false;
        pendingSpawnIntro = false;

        if (currentState == State.Hopping)
        {
            nextHopTime = Mathf.Max(nextHopTime, Time.time + hopInterval);
        }
        else if (currentState == State.Attacking)
        {
            damageDealtThisAttack = false;
        }
        else if (currentState == State.Dead)
        {
            deathExecuted = false;
            if (rb != null)
            {
                rb.velocity = Vector2.zero;
            }
        }

        ApplyAnimatorParameters();

        if (currentState == State.Attacking)
        {
            stateRoutine = StartCoroutine(AttackLoop());
        }
        else if (currentState == State.Dead)
        {
            stateRoutine = StartCoroutine(DestroyAfterDeathAnimation());
        }
    }

    private IEnumerator AttackLoop()
    {
        bool firstCycle = true;

        while (currentState == State.Attacking)
        {
            damageDealtThisAttack = false;
            FacePlayer();

            if (!firstCycle && animator != null)
            {
                animator.SetBool(AttackingParameter, false);
                yield return null;
                animator.SetBool(AttackingParameter, true);
            }

            yield return new WaitForSeconds(GetClipWaitTime(AttackClipName));

            if (currentState != State.Attacking)
            {
                yield break;
            }

            if (!IsPlayerWithinAttackRange())
            {
                if (!damageDealtThisAttack && playAttackMissSfx && attackMissSfx != null)
                {
                    AudioManager.PlaySfxAt(attackMissSfx, transform.position, attackMissSfxVolume);
                }

                stateRoutine = null;
                EnterState(State.Hopping);
                yield break;
            }

            if (!damageDealtThisAttack && playAttackMissSfx && attackMissSfx != null)
            {
                AudioManager.PlaySfxAt(attackMissSfx, transform.position, attackMissSfxVolume);
            }

            firstCycle = false;
        }
    }

    private IEnumerator DestroyAfterDeathAnimation()
    {
        yield return new WaitForSeconds(GetClipWaitTime(DeathClipName));

        if (currentState != State.Dead)
        {
            yield break;
        }

        stateRoutine = null;
        Destroy(gameObject);
    }

    private void DieImmediately()
    {
        if (stateRoutine != null)
        {
            StopCoroutine(stateRoutine);
            stateRoutine = null;
        }

        currentState = State.Dead;
        isSpawnIntroPlaying = false;
        pendingSpawnIntro = false;
        DisableAndHide();
        Destroy(gameObject, destroyDelayOnHit);
    }

    private void ApplyAnimatorParameters()
    {
        if (animator == null)
        {
            return;
        }

        animator.SetBool(HoppingParameter, currentState == State.Hopping);
        animator.SetBool(AttackingParameter, currentState == State.Attacking);
        animator.SetBool(DeadParameter, currentState == State.Dead);
    }

    private void AcquirePlayerIfNeeded()
    {
        if (playerTransform != null)
        {
            return;
        }

        if (PlayerControlls.Instance != null)
        {
            playerTransform = PlayerControlls.Instance.transform;
            return;
        }

        GameObject taggedPlayer = GameObject.FindWithTag("Player");
        if (taggedPlayer != null)
        {
            playerTransform = taggedPlayer.transform;
        }
    }

    private void PerformHop()
    {
        if (playerTransform == null || rb == null)
        {
            return;
        }

        float direction = playerTransform.position.x >= transform.position.x ? 1f : -1f;
        FaceDirection(direction);

        rb.velocity = new Vector2(direction * hopForceX, hopForceY);
        if (playHopSfx && hopSfx != null)
        {
            AudioManager.PlaySfxAt(hopSfx, transform.position, hopSfxVolume);
        }
        nextHopTime = Time.time + hopInterval;
    }

    private bool IsGrounded()
    {
        if (primaryCollider == null)
        {
            return false;
        }

        Bounds bounds = primaryCollider.bounds;
        Vector2 checkPosition = new Vector2(bounds.center.x, bounds.min.y - groundCheckOffset);
        return Physics2D.OverlapCircle(checkPosition, groundCheckRadius, groundLayer);
    }

    private bool IsPlayerWithinAttackRange()
    {
        if (playerTransform == null)
        {
            return false;
        }

        return Vector2.Distance(transform.position, playerTransform.position) <= attackRange;
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
    }

    private void ApplyDamageToPlayer()
    {
        if (playerTransform == null)
        {
            return;
        }

        if (HasPlayerTakeDamageReceiver())
        {
            playerTransform.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
            return;
        }

        PlayerControlls playerControlls = playerTransform.GetComponent<PlayerControlls>();
        if (playerControlls != null)
        {
            playerControlls.TakeSanityDamage(damage);
        }
    }

    private bool HasPlayerTakeDamageReceiver()
    {
        if (playerTransform == null)
        {
            return false;
        }

        MonoBehaviour[] receivers = playerTransform.GetComponents<MonoBehaviour>();
        for (int receiverIndex = 0; receiverIndex < receivers.Length; receiverIndex++)
        {
            MonoBehaviour receiver = receivers[receiverIndex];
            if (receiver == null)
            {
                continue;
            }

            MethodInfo[] methods = receiver.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int methodIndex = 0; methodIndex < methods.Length; methodIndex++)
            {
                MethodInfo method = methods[methodIndex];
                if (!string.Equals(method.Name, "TakeDamage", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1)
                {
                    continue;
                }

                Type parameterType = parameters[0].ParameterType;
                if (parameterType == typeof(int) || parameterType == typeof(object))
                {
                    return true;
                }
            }
        }

        return false;
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

    private void BeginSpawnIntro()
    {
        if (!spawnActive || currentState == State.Dead)
        {
            return;
        }

        if (stateRoutine != null)
        {
            StopCoroutine(stateRoutine);
            stateRoutine = null;
        }

        isSpawnIntroPlaying = true;

        if (animator != null)
        {
            animator.SetBool(HoppingParameter, false);
            animator.SetBool(AttackingParameter, false);
            animator.SetBool(DeadParameter, false);

            int introStateHash = Animator.StringToHash(spawnIntroStateName);
            if (animator.HasState(0, introStateHash))
            {
                animator.Play(introStateHash, 0, 0f);
                animator.Update(0f);
            }
        }

        stateRoutine = StartCoroutine(SpawnIntroRoutine());
    }

    private void DisableAndHide()
    {
        deathExecuted = true;

        if (animator != null)
        {
            animator.enabled = false;
        }

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

    private IEnumerator SpawnIntroRoutine()
    {
        yield return new WaitForSeconds(GetClipWaitTime(DeathClipName));

        if (!spawnActive || currentState == State.Dead)
        {
            yield break;
        }

        isSpawnIntroPlaying = false;
        stateRoutine = null;
        nextHopTime = Time.time + hopInterval;
        EnterState(State.Hopping);
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

            SmallRabbitDamageRelay relay = collider.GetComponent<SmallRabbitDamageRelay>();
            if (relay == null)
            {
                relay = collider.gameObject.AddComponent<SmallRabbitDamageRelay>();
            }

            relay.Bind(this);
        }
    }

    private void ApplySpawnActiveState()
    {
        if (animator != null)
        {
            animator.enabled = spawnActive;
        }

        for (int index = 0; index < cachedSpriteRenderers.Length; index++)
        {
            if (cachedSpriteRenderers[index] != null)
            {
                cachedSpriteRenderers[index].enabled = spawnActive;
            }
        }

        for (int index = 0; index < cachedColliders.Length; index++)
        {
            if (cachedColliders[index] != null)
            {
                cachedColliders[index].enabled = spawnActive;
            }
        }

        if (rb != null)
        {
            rb.simulated = spawnActive;
            if (!spawnActive)
            {
                rb.velocity = Vector2.zero;
            }
        }
    }

    private void PrepareAnimatorForSpawnIntro()
    {
        if (animator == null || !spawnActive)
        {
            return;
        }

        animator.enabled = true;
        animator.Rebind();
        animator.Update(0f);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Collider2D collider = primaryCollider != null ? primaryCollider : GetComponent<Collider2D>();
        if (collider == null)
        {
            return;
        }

        Bounds bounds = collider.bounds;
        Vector2 checkPosition = new Vector2(bounds.center.x, bounds.min.y - groundCheckOffset);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(checkPosition, groundCheckRadius);
    }
}

internal sealed class SmallRabbitDamageRelay : MonoBehaviour
{
    private SmallRabbit owner;

    public void Bind(SmallRabbit smallRabbit)
    {
        owner = smallRabbit;
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