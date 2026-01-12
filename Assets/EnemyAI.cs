using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class EnemyAI : MonoBehaviour
{
    private enum State { Idle, Chasing, Attacking, Returning }
    private State state = State.Idle;

    [Header("References")]
    [Tooltip("Optional explicit player reference. If null, uses PlayerControlls.Instance or object tagged 'Player'.")]
    [SerializeField] private Transform player;

    [Header("Detection")]
    [SerializeField] private float detectionRange = 8f;
    [SerializeField] private float chaseDetectionRange = 12f;
    [SerializeField] private float verticalTolerance = 3f;
    [SerializeField] private LayerMask lineOfSightBlockers;
    [SerializeField] private bool requireLineOfSight = false;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float returnSpeed = 3f;
    [SerializeField] private bool flipSprite = true;
    [SerializeField] private float returnStopThreshold = 0.05f;

    [Header("Attack")]
    [SerializeField] private float attackRange = 1.2f;
    [SerializeField] private float attackCooldown = 1.0f;
    [SerializeField] private int sanityDamage = 10;
    [SerializeField] private float attackWindup = 0.25f;
    private float lastAttackTime = -999f;

    [Header("Behavior Tuning")]
    [Tooltip("Extra buffer distance before abandoning chase (hysteresis). 0 = switch exactly at chaseDetectionRange.")]
    [SerializeField] private float chaseDropBuffer = 1.0f;

    [Header("Debug Visuals")]
    [Tooltip("When enabled, the enemy tints its SpriteRenderer by state and auto-creates a debug sprite if missing.")]
    [SerializeField] private bool useDebugColors = false;
    [SerializeField] private Color idleColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    [SerializeField] private Color chaseColor = new Color(0.6f, 0.4f, 0.2f, 1f);
    [SerializeField] private Color attackColor = new Color(0.8f, 0.2f, 0.2f, 1f);
    [SerializeField] private Color returningColor = new Color(0.2f, 0.4f, 0.8f, 1f);

    [Header("Animator")]
    [SerializeField] private Animator animator;
    [SerializeField] private string idleStateName = "Enemy_Idle";
    [SerializeField] private string walkBoolParameter = "Enemy_Walk";     // bool
    [SerializeField] private string attackTriggerParameter = "Enemy_Attack"; // trigger

    [Header("Audio - Enemy SFX")]
    [SerializeField] private AudioClip enemyAttackHitSfx;
    [SerializeField] private AudioClip enemyAttackMissSfx;

    private Rigidbody2D rb;
    private SpriteRenderer sr;
    private Collider2D col;
    private Vector2 spawnPosition;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        sr = GetComponent<SpriteRenderer>();
        if (animator == null) animator = GetComponent<Animator>(); // auto-bind if present

        // Record spawn on Awake
        spawnPosition = transform.position;

        // Only auto-add a simple square sprite if debug visuals are enabled and missing
        if (useDebugColors && sr == null)
        {
            var spriteGO = new GameObject("Sprite");
            spriteGO.transform.SetParent(transform, false);
            sr = spriteGO.AddComponent<SpriteRenderer>();
            sr.sprite = GenerateDebugSprite();
        }

        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f; // stays on plane for now
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        }

        // Ensure collider is a BoxCollider2D for rectangular shape
        if (!(col is BoxCollider2D))
        {
            Destroy(col);
            col = gameObject.AddComponent<BoxCollider2D>();
        }

        UpdateVisualState();
        UpdateAnimatorState();
    }

    private void Update()
    {
        AcquirePlayerIfNeeded();

        // State transitions
        switch (state)
        {
            case State.Idle:
                if (PlayerWithinDetection(false))
                    state = State.Chasing;
                break;

            case State.Chasing:
                // If we lose player beyond chase range + buffer, return to spawn
                if (!PlayerWithinDetection(true))
                {
                    state = State.Returning;
                }
                else if (InAttackRange() && Time.time >= lastAttackTime + attackCooldown)
                {
                    StartCoroutine(AttackRoutine());
                }
                break;

            case State.Attacking:
                // movement handled in routine
                break;

            case State.Returning:
                // If we can see the player again while returning, resume chase
                if (PlayerWithinDetection(false))
                {
                    state = State.Chasing;
                }
                else if (AtSpawn())
                {
                    state = State.Idle;
                }
                break;
        }

        UpdateVisualState();
        UpdateAnimatorState();
    }

    private void FixedUpdate()
    {
        switch (state)
        {
            case State.Chasing:
                if (player == null) return;
                float dx = player.position.x - transform.position.x;
                float dir = Mathf.Sign(dx);
                rb.velocity = new Vector2(dir * moveSpeed, 0f);
                ApplyFlip(dir);
                break;

            case State.Returning:
                // Move horizontally back to spawn
                float backDx = spawnPosition.x - transform.position.x;
                float backDir = Mathf.Sign(backDx);
                // Stop if very close to spawn (avoid jitter)
                if (Mathf.Abs(backDx) <= returnStopThreshold)
                {
                    rb.velocity = Vector2.zero;
                    transform.position = new Vector3(spawnPosition.x, transform.position.y, transform.position.z);
                }
                else
                {
                    rb.velocity = new Vector2(backDir * returnSpeed, 0f);
                    ApplyFlip(backDir);
                }
                break;

            default:
                // Idle / Attacking: do not move
                rb.velocity = Vector2.zero;
                break;
        }
    }

    private IEnumerator AttackRoutine()
    {
        state = State.Attacking;
        UpdateVisualState();
        rb.velocity = Vector2.zero;

        // Trigger attack animation
        if (animator != null && !string.IsNullOrEmpty(attackTriggerParameter))
        {
            animator.ResetTrigger(attackTriggerParameter); // safety
            animator.SetTrigger(attackTriggerParameter);
        }

        lastAttackTime = Time.time;
        yield return new WaitForSeconds(attackWindup);

        // Re-check range before applying damage
        bool hit = false;
        if (player != null && InAttackRange())
        {
            hit = true;
            if (PlayerControlls.Instance != null) PlayerControlls.Instance.TakeSanityDamage(sanityDamage);
            else { var taggedPlayer = GameObject.FindWithTag("Player"); if (taggedPlayer != null) Debug.Log("Player hit (no PlayerControlls.Instance found)."); }
        }
        // SFX after deciding hit/miss
        var clip = hit ? enemyAttackHitSfx : enemyAttackMissSfx;
        if (clip != null) AudioManager.PlaySfxAt(clip, transform.position, 1f);

        // Wait remaining cooldown before chasing again
        float remaining = Mathf.Max(0f, (lastAttackTime + attackCooldown) - Time.time);
        if (remaining > 0f) yield return new WaitForSeconds(remaining);

        // Resume chasing if still within chase detection; else return/idle
        if (PlayerWithinDetection(true))
            state = State.Chasing;
        else
            state = State.Returning;

        UpdateVisualState();
        UpdateAnimatorState();
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

    // When chasing, use chaseDetectionRange; otherwise use normal detectionRange.
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

    private bool AtSpawn()
    {
        return Mathf.Abs(transform.position.x - spawnPosition.x) <= returnStopThreshold;
    }

    private void ApplyFlip(float dir)
    {
        if (!flipSprite || Mathf.Abs(dir) <= 0.01f) return;
        Vector3 s = transform.localScale;
        s.x = Mathf.Abs(s.x) * (dir > 0 ? 1f : -1f);
        transform.localScale = s;
    }

    private void UpdateVisualState()
    {
        if (!useDebugColors || sr == null) return;

        switch (state)
        {
            case State.Idle: sr.color = idleColor; break;
            case State.Chasing: sr.color = chaseColor; break;
            case State.Attacking: sr.color = attackColor; break;
            case State.Returning: sr.color = returningColor; break;
        }
    }

    private void UpdateAnimatorState()
    {
        if (animator == null) return;

        // Walking when we have horizontal velocity (chasing/returning)
        bool isWalking = Mathf.Abs(rb != null ? rb.velocity.x : 0f) > 0.01f;

        // Set walk bool
        if (!string.IsNullOrEmpty(walkBoolParameter))
            animator.SetBool(walkBoolParameter, isWalking);

        // If not walking and not attacking, ensure we are in idle (optional instant play)
        if (!isWalking && state != State.Attacking && !string.IsNullOrEmpty(idleStateName))
        {
            int idleHash = Animator.StringToHash(idleStateName);
            if (animator.HasState(0, idleHash))
            {
                // keep smooth if already on idle; otherwise instant snap is fine
                animator.Play(idleHash, 0);
            }
        }
    }

    // Creates a simple 1x1 white square sprite (then tinted by color)
    private Sprite GenerateDebugSprite()
    {
        Texture2D tex = new Texture2D(2, 2);
        tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 32);
    }

    private void OnDrawGizmosSelected()
    {
        // Show normal detection and chase detection spheres
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, chaseDetectionRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Spawn marker
        Gizmos.color = Color.cyan;
        Vector3 spawn = Application.isPlaying ? (Vector3)spawnPosition : transform.position;
        Gizmos.DrawWireCube(new Vector3(spawn.x, spawn.y, spawn.z), new Vector3(0.2f, 0.2f, 0.2f));
    }
}
