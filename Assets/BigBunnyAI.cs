using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class BigBunnyAI : MonoBehaviour
{
    private enum State { Idle, Chasing, Hopping, Attacking }
    private State state = State.Idle;

    [Header("References")]
    [Tooltip("Optional explicit player reference. If null, uses PlayerControlls.Instance or object tagged 'Player'.")]
    [SerializeField] private Transform player;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.12f;
    [SerializeField] private LayerMask groundLayer;

    [Header("Detection")]
    [SerializeField] private float detectionRange = 10f;
    [SerializeField] private float chaseDetectionRange = 14f;
    [SerializeField] private float verticalTolerance = 3f;
    [SerializeField] private LayerMask lineOfSightBlockers;
    [SerializeField] private bool requireLineOfSight = false;
    [Tooltip("Extra buffer distance before abandoning chase (hysteresis). 0 = switch exactly at chaseDetectionRange.")]
    [SerializeField] private float chaseDropBuffer = 1.0f;

    [Header("Hop Movement")]
    [Tooltip("Horizontal speed maintained while mid-air during a hop.")]
    [SerializeField] private float hopHorizontalSpeed = 5.5f;
    [Tooltip("Vertical impulse for each hop.")]
    [SerializeField] private float hopForceY = 9.5f;
    [Tooltip("Minimum time between hops (seconds).")]
    [SerializeField] private float hopCooldown = 0.65f;
    [Tooltip("If true, flips sprite along X to face hop direction.")]
    [SerializeField] private bool flipSprite = true;

    [Header("Attack")]
    [SerializeField] private float attackRange = 1.3f;
    [SerializeField] private float attackCooldown = 1.0f;
    [SerializeField] private int sanityDamage = 15;
    [SerializeField] private float attackWindup = 0.25f;

    [Header("Animator")]
    [SerializeField] private Animator animator;
    [SerializeField] private string idleStateName = "Bunny_Idle";
    [SerializeField] private string jumpTriggerParameter = "Bunny_Jump";      // trigger
    [SerializeField] private string attackTriggerParameter = "Bunny_Attack";  // trigger
    [SerializeField] private string groundedBoolParameter = "grounded";       // bool (optional)
    [SerializeField] private string yVelFloatParameter = "yVel";              // float (optional)

    [Header("Debug Visuals")]
    [SerializeField] private bool useDebugColors = false;
    [SerializeField] private Color idleColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    [SerializeField] private Color chaseColor = new Color(0.6f, 0.4f, 0.2f, 1f);
    [SerializeField] private Color hopColor = new Color(0.3f, 0.7f, 0.4f, 1f);
    [SerializeField] private Color attackColor = new Color(0.8f, 0.2f, 0.2f, 1f);

    [Header("Audio - Enemy SFX")]
    [SerializeField] private AudioClip enemyAttackHitSfx;
    [SerializeField] private AudioClip enemyAttackMissSfx;
    [Tooltip("Sound played when the bunny starts a hop.")]
    [SerializeField] private AudioClip bunnyJumpSfx;

    private Rigidbody2D rb;
    private Collider2D col;
    private SpriteRenderer sr;

    private bool isGrounded;
    private float lastHopTime = -999f;
    private float lastAttackTime = -999f;
    private float desiredHopDir = 1f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        sr = GetComponent<SpriteRenderer>();
        if (animator == null) animator = GetComponent<Animator>();

        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
        }
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        if (rb.gravityScale <= 0f) rb.gravityScale = 3.0f; // ensure we actually fall

        // Ensure a sensible collider exists (prefer box)
        if (col == null || !(col is BoxCollider2D))
        {
            if (col != null) Destroy(col);
            col = gameObject.AddComponent<BoxCollider2D>();
        }

        UpdateGrounded();
        UpdateVisualState();
        PlayIdleIfPossible();
    }

    private void Update()
    {
        AcquirePlayerIfNeeded();
        UpdateGrounded();
        UpdateAirAnimParams();

        switch (state)
        {
            case State.Idle:
                if (PlayerWithinDetection(false))
                    state = State.Chasing;
                break;

            case State.Chasing:
                if (!PlayerWithinDetection(true))
                {
                    state = State.Idle;
                }
                else if (InAttackRange() && Time.time >= lastAttackTime + attackCooldown)
                {
                    StartCoroutine(AttackRoutine());
                }
                else if (isGrounded && Time.time >= lastHopTime + hopCooldown)
                {
                    StartCoroutine(HopRoutine());
                }
                break;

            case State.Hopping:
                // Landing detection in FixedUpdate/UpdateGrounded will return state to Chasing
                break;

            case State.Attacking:
                // Controlled by AttackRoutine
                break;
        }

        UpdateVisualState();
    }

    private void FixedUpdate()
    {
        // Maintain horizontal speed while hopping to produce clean parabolic arcs
        if (state == State.Hopping)
        {
            rb.velocity = new Vector2(desiredHopDir * hopHorizontalSpeed, rb.velocity.y);
        }

        // Detect landing to end hop
        if (state == State.Hopping && isGrounded && rb.velocity.y <= 0.01f)
        {
            state = PlayerWithinDetection(true) ? State.Chasing : State.Idle;
            UpdateVisualState();
            PlayIdleIfPossible();
        }
    }

    private IEnumerator HopRoutine()
    {
        if (player == null) yield break;

        state = State.Hopping;
        lastHopTime = Time.time;

        float dx = player.position.x - transform.position.x;
        desiredHopDir = Mathf.Sign(dx != 0 ? dx : 1f);
        ApplyFlip(desiredHopDir);

        // Optional jump trigger
        if (animator != null && !string.IsNullOrEmpty(jumpTriggerParameter))
        {
            animator.ResetTrigger(jumpTriggerParameter);
            animator.SetTrigger(jumpTriggerParameter);
        }

        // Small pre-hop settle to avoid instantly stacking forces this frame
        yield return new WaitForFixedUpdate();

        // Start hop impulse + SFX
        Vector2 v = rb.velocity;
        v.x = desiredHopDir * hopHorizontalSpeed;
        v.y = 0f;
        rb.velocity = v;
        rb.AddForce(Vector2.up * hopForceY, ForceMode2D.Impulse);

        if (bunnyJumpSfx != null)
            AudioManager.PlaySfxAt(bunnyJumpSfx, transform.position, 1f);

        // Remain in Hopping until we land (handled in FixedUpdate)
        // Safety timeout in case of odd collisions
        float safety = Time.time + 3.0f;
        while (state == State.Hopping && Time.time < safety)
        {
            yield return null;
        }

        if (state == State.Hopping) // timed out
        {
            state = PlayerWithinDetection(true) ? State.Chasing : State.Idle;
            UpdateVisualState();
            PlayIdleIfPossible();
        }
    }

    private IEnumerator AttackRoutine()
    {
        state = State.Attacking;
        UpdateVisualState();
        rb.velocity = new Vector2(0f, rb.velocity.y); // stop horizontal drift

        if (animator != null && !string.IsNullOrEmpty(attackTriggerParameter))
        {
            animator.ResetTrigger(attackTriggerParameter);
            animator.SetTrigger(attackTriggerParameter);
        }

        lastAttackTime = Time.time;
        yield return new WaitForSeconds(attackWindup);

        bool hit = false;
        if (player != null && InAttackRange())
        {
            hit = true;
            if (PlayerControlls.Instance != null) PlayerControlls.Instance.TakeSanityDamage(sanityDamage);
            else { var taggedPlayer = GameObject.FindWithTag("Player"); if (taggedPlayer != null) Debug.Log("BigBunny hit the player (no PlayerControlls.Instance found)."); }
        }
        var clip = hit ? enemyAttackHitSfx : enemyAttackMissSfx;
        if (clip != null) AudioManager.PlaySfxAt(clip, transform.position, 1f);

        // Finish cooldown before resuming chase
        float remaining = Mathf.Max(0f, (lastAttackTime + attackCooldown) - Time.time);
        if (remaining > 0f) yield return new WaitForSeconds(remaining);

        state = PlayerWithinDetection(true) ? State.Chasing : State.Idle;
        UpdateVisualState();
        PlayIdleIfPossible();
    }

    private void AcquirePlayerIfNeeded()
    {
        if (player != null) return;

        if (PlayerControlls.Instance != null)
        {
            player = PlayerControlls.Instance.transform;
            return;
        }

        var tagged = GameObject.FindWithTag("Player");
        if (tagged != null) player = tagged.transform;
    }

    private void UpdateGrounded()
    {
        if (groundCheck == null)
        {
            isGrounded = false;
        }
        else
        {
            isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        }
    }

    private bool PlayerWithinDetection(bool useChaseRange)
    {
        if (player == null) return false;
        float range = useChaseRange ? (chaseDetectionRange + chaseDropBuffer) : detectionRange;

        float dx = Mathf.Abs(player.position.x - transform.position.x);
        float dy = Mathf.Abs(player.position.y - transform.position.y);
        if (dx > range || dy > verticalTolerance) return false;

        if (requireLineOfSight && lineOfSightBlockers.value != 0)
        {
            Vector2 origin = transform.position;
            Vector2 target = player.position;
            Vector2 dir = (target - origin).normalized;
            float dist = Vector2.Distance(origin, target);
            RaycastHit2D hit = Physics2D.Raycast(origin, dir, dist, lineOfSightBlockers);
            if (hit.collider != null) return false;
        }

        return true;
    }

    private bool InAttackRange()
    {
        if (player == null) return false;
        float dx = Mathf.Abs(player.position.x - transform.position.x);
        float dy = Mathf.Abs(player.position.y - transform.position.y);
        return dx <= attackRange && dy <= verticalTolerance;
    }

    private void ApplyFlip(float dir)
    {
        if (!flipSprite || Mathf.Abs(dir) <= 0.01f) return;
        Vector3 s = transform.localScale;
        s.x = Mathf.Abs(s.x) * (dir > 0f ? 1f : -1f);
        transform.localScale = s;
    }

    private void UpdateVisualState()
    {
        if (!useDebugColors || sr == null) return;

        switch (state)
        {
            case State.Idle:     sr.color = idleColor; break;
            case State.Chasing:  sr.color = chaseColor; break;
            case State.Hopping:  sr.color = hopColor; break;
            case State.Attacking:sr.color = attackColor; break;
        }
    }

    private void PlayIdleIfPossible()
    {
        if (animator == null || string.IsNullOrEmpty(idleStateName)) return;
        int idleHash = Animator.StringToHash(idleStateName);
        if (animator.HasState(0, idleHash))
            animator.Play(idleHash, 0, 0f);
    }

    private void UpdateAirAnimParams()
    {
        if (animator == null) return;

        if (!string.IsNullOrEmpty(groundedBoolParameter))
            animator.SetBool(groundedBoolParameter, isGrounded);

        if (!string.IsNullOrEmpty(yVelFloatParameter))
            animator.SetFloat(yVelFloatParameter, rb != null ? rb.velocity.y : 0f);
    }

    private void OnDrawGizmosSelected()
    {
        // Detection
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, chaseDetectionRange);

        // Attack
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Ground check
        if (groundCheck != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
}
