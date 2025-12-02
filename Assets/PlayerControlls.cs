using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System; // for Action

public class PlayerControlls : MonoBehaviour
{
    // ---- Components & runtime state ----
    private Rigidbody2D rb;
    Animator anim;

    // ---- Movement ----
    [Header("Horizontal Movement Settings")]
    [SerializeField] private float walkSpeed = 5f;
    private float xAxis;
    private float lastMoveDir = 1f;

    [Header("Air Movement")]
    [SerializeField] private float airAcceleration = 20f;
    [SerializeField] [Range(0f, 1f)] private float airControlMultiplier = 0.9f;
    [SerializeField] [Range(0f, 1f)] private float airDecelerationMultiplier = 0.25f;
    [SerializeField] private float airTurnMultiplier = 3f;

    [Header("Jump Settings")]
    [SerializeField] private float jumpForce = 10f;
    [SerializeField] [Range(0f, 1f)] private float secondJumpMultiplier = 0.4f;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.1f;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private int maxJumps = 2;
    private int jumpsLeft;
    private bool isGrounded;
    private bool wasGrounded = false;

    [Header("Fast Fall Settings")]
    [SerializeField] private float fastFallForce = 30f;

    [Header("Dash Settings")]
    [SerializeField] private KeyCode dashKey = KeyCode.LeftShift;
    [SerializeField] private float dashSpeed = 18f;
    [SerializeField] private float dashDuration = 0.15f;
    [SerializeField] private float dashCooldown = 0.25f;
    [SerializeField] private string dashTriggerParameter = "dash";

    [Header("Action Lock / Post-dash")]
    [SerializeField] private float actionLockDuration = 0.12f;
    [SerializeField] private float postDashHangDuration = 0.12f;
    [SerializeField] [Range(0f, 1f)] private float postDashGravityMultiplier = 0.35f;
    [SerializeField] private float postDashSpeedMultiplier = 0.9f;
    [SerializeField] private float postDashUpwardBoost = 0.2f;
    [SerializeField] private float postDashDecelDuration = 0.25f;

    private bool isDashing = false;
    private bool isPostDashHang = false;
    private float lastDashTime = -999f;
    private float actionLockedUntil = 0f;

    [Header("Input / Movement Tweaks")]
    [SerializeField] private float inputHoldAfterJump = 0.12f;
    private float inputHoldTimer = 0f;
    private float retainedXAxis = 0f;

    [Header("Debug / Physics")]
    [SerializeField] private bool enableRigidbodyInterpolation = true;
    private float originalGravityScale = 1f;

    // ---- Attack ----
    [Header("Attack Settings")]
    [SerializeField] private KeyCode attackKey = KeyCode.J;
    [SerializeField] private float attackCooldown = 0.35f;
    [SerializeField] private float attackDuration = 0.12f;
    [SerializeField] private float attackRange = 0.8f;
    [SerializeField] private Vector2 attackBoxSize = new Vector2(1.0f, 0.6f);
    [SerializeField] private LayerMask attackLayer;
    [SerializeField] private int attackDamage = 1;

    [Header("Per-side Attack Ranges (<=0 uses default)")]
    [SerializeField] private float attackRangeRight = 0.8f;
    [SerializeField] private float attackRangeLeft = 0.8f;
    [SerializeField] private float attackRangeUp = 0.8f;
    [SerializeField] private float attackRangeDown = 0.8f;

    [Header("Per-side Attack Box Sizes (Vector2.zero uses default)")]
    [SerializeField] private Vector2 attackBoxSizeRight = new Vector2(1.0f, 0.6f);
    [SerializeField] private Vector2 attackBoxSizeLeft = new Vector2(1.0f, 0.6f);
    [SerializeField] private Vector2 attackBoxSizeUp = new Vector2(1.0f, 0.6f);
    [SerializeField] private Vector2 attackBoxSizeDown = new Vector2(1.0f, 0.6f);

    private float lastAttackTime = -999f;

    public static PlayerControlls Instance;
    private const float runInputThreshold = 0.01f;

    [Header("Facing / Turn Animation")]
    [SerializeField] private string facingAnimatorParameter = "facingRight";
    [SerializeField] private bool useSpriteFlip = true;
    private bool isFacingRight = true;

    // ---- Sanity ----
    [Header("Sanity")]
    [SerializeField] private int maxSanity = 100;
    [SerializeField] private int sanity = 100;

    [Header("Hub Sanity Settings")]
    [SerializeField] private string hubSceneName = "Hub";
    [SerializeField] private int hubFirstEnterSanity = 50;
    [SerializeField] private int hubReturnBonus = 30;

    private const string PrefKeySanity = "PLAYER_SANITY";
    private const string PrefKeyHubVisited = "HUB_VISITED";

    [Tooltip("Optional: assign UI Slider. If null, a temp persistent one is created.")]
    [SerializeField] private Slider sanitySlider;

    [Header("Save / Testing")]
    [Tooltip("If true, clears saved sanity and hub visit flag when Play starts.")]
    [SerializeField] private bool resetSavesOnPlay = false;

    public static Action<int,int> OnSanityChanged; // current, max

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Optional: reset saves when entering Play mode (useful for testing first-run behavior)
        if (resetSavesOnPlay)
        {
            PlayerPrefs.DeleteKey(PrefKeySanity);
            PlayerPrefs.DeleteKey(PrefKeyHubVisited);
            PlayerPrefs.Save();
        }

        // Load persisted sanity (or keep serialized default if none)
        if (PlayerPrefs.HasKey(PrefKeySanity))
            sanity = Mathf.Clamp(PlayerPrefs.GetInt(PrefKeySanity, sanity), 0, maxSanity);
        else
            sanity = Mathf.Clamp(sanity, 0, maxSanity);

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        jumpsLeft = maxJumps;

        if (rb != null)
        {
            originalGravityScale = rb.gravityScale;
            if (enableRigidbodyInterpolation)
                rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        isFacingRight = transform.localScale.x < 0f ? false : (lastMoveDir >= 0f);
        if (anim != null && !string.IsNullOrEmpty(facingAnimatorParameter))
            anim.SetBool(facingAnimatorParameter, isFacingRight);

        sanity = Mathf.Clamp(sanity, 0, maxSanity);
        EnsureSanityUI();
        UpdateSanityUI();
        PersistSanity();

        OnSanityChanged?.Invoke(sanity, maxSanity);
    }

    private void Update()
    {
        CheckGrounded();
        ReadInputs();
        HandleJumpInput();
        HandleDashInput();
        HandleAttackInput();
    }

    private void FixedUpdate()
    {
        Move();
        FastFallCheck();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureSanityUI();
        UpdateSanityUI();

        if (string.Equals(scene.name, hubSceneName))
        {
            bool hubVisited = PlayerPrefs.GetInt(PrefKeyHubVisited, 0) == 1;
            if (!hubVisited)
            {
                SetSanity(hubFirstEnterSanity);
                PlayerPrefs.SetInt(PrefKeyHubVisited, 1);
                PlayerPrefs.Save();
            }
            else
            {
                RestoreSanity(hubReturnBonus);
            }
        }

        ApplySceneSpawn();
        PersistSanity();

        // NEW: always notify listeners after a scene load
        OnSanityChanged?.Invoke(sanity, maxSanity);
    }

    private void ApplySceneSpawn()
    {
        string nextId = SceneSpawnState.NextSpawnId;
        if (string.IsNullOrEmpty(nextId)) return;

        var spawns = GameObject.FindObjectsOfType<PlayerSpawn>();
        PlayerSpawn match = spawns.FirstOrDefault(s => s.spawnId == nextId);
        if (match == null)
            match = spawns.FirstOrDefault(s => s.spawnId == "default");

        if (match != null)
            transform.position = match.transform.position;

        SceneSpawnState.NextSpawnId = null;
    }

    // ---- Input handlers ----
    private void ReadInputs()
    {
        if (inputHoldTimer > 0f)
        {
            inputHoldTimer -= Time.deltaTime;
            xAxis = retainedXAxis;
        }
        else
        {
            xAxis = Input.GetAxisRaw("Horizontal");
        }

        if (Mathf.Abs(xAxis) > 0.01f) lastMoveDir = Mathf.Sign(xAxis);
    }

    private void HandleJumpInput()
    {
        if (Time.time < actionLockedUntil) return;
        if (Input.GetButtonDown("Jump") && jumpsLeft > 0 && !isDashing && !isPostDashHang)
        {
            Jump();
            jumpsLeft--;
            actionLockedUntil = Time.time + actionLockDuration;
        }
    }

    private void HandleDashInput()
    {
        if (Input.GetKeyDown(dashKey) && !isDashing && Time.time >= lastDashTime + dashCooldown && Time.time >= actionLockedUntil)
        {
            StartCoroutine(Dash());
        }
    }

    private void HandleAttackInput()
    {
        if ((Input.GetKeyDown(attackKey) || Input.GetButtonDown("Fire1")) &&
            Time.time >= lastAttackTime + attackCooldown &&
            Time.time >= actionLockedUntil)
        {
            Attack();
        }
    }

    // ---- Movement ----
    private void Move()
    {
        if (anim != null)
        {
            bool isRunning = !isDashing && !isPostDashHang && isGrounded && Mathf.Abs(xAxis) > runInputThreshold;
            anim.SetBool("running", isRunning);
        }

        if (Mathf.Abs(xAxis) > runInputThreshold)
        {
            bool wantRight = xAxis > 0f;
            if (wantRight != isFacingRight)
                SetFacing(wantRight);
        }

        if (isDashing || (isPostDashHang && !isGrounded)) return;

        float targetX = xAxis * walkSpeed;

        if (isGrounded)
        {
            rb.velocity = new Vector2(targetX, rb.velocity.y);
        }
        else
        {
            float airTargetX = xAxis * walkSpeed * airControlMultiplier;
            float currentX = rb.velocity.x;
            float accel = (Mathf.Abs(xAxis) > 0.01f) ? airAcceleration : airAcceleration * airDecelerationMultiplier;

            if (Mathf.Abs(xAxis) > 0.01f && currentX * airTargetX < 0f) accel *= airTurnMultiplier;

            float newX = Mathf.MoveTowards(currentX, airTargetX, accel * Time.fixedDeltaTime);
            rb.velocity = new Vector2(newX, rb.velocity.y);
        }
    }

    private void SetFacing(bool faceRight)
    {
        isFacingRight = faceRight;
        if (anim != null && !string.IsNullOrEmpty(facingAnimatorParameter))
            anim.SetBool(facingAnimatorParameter, isFacingRight);
        if (useSpriteFlip)
            FlipSprite();
    }

    private void FlipSprite()
    {
        Vector3 s = transform.localScale;
        s.x = Mathf.Abs(s.x) * (isFacingRight ? 1f : -1f);
        transform.localScale = s;
    }

    private void Jump()
    {
        if (anim != null)
            anim.SetBool("jumping", true);

        retainedXAxis = xAxis;
        inputHoldTimer = inputHoldAfterJump;

        float force = (jumpsLeft == maxJumps) ? jumpForce : jumpForce * secondJumpMultiplier;
        rb.velocity = new Vector2(rb.velocity.x, 0f);
        rb.AddForce(Vector2.up * force, ForceMode2D.Impulse);
    }

    private IEnumerator Dash()
    {
        if (anim != null && !string.IsNullOrEmpty(dashTriggerParameter))
            anim.SetTrigger(dashTriggerParameter);

        isDashing = true;
        lastDashTime = Time.time;
        actionLockedUntil = Time.time + actionLockDuration;

        float dir = DetermineDashDirection();
        float dashEnd = Time.time + dashDuration;

        rb.velocity = new Vector2(dir * dashSpeed, 0f);
        rb.gravityScale = 0f;

        while (Time.time < dashEnd)
        {
            rb.velocity = new Vector2(dir * dashSpeed, 0f);
            yield return new WaitForFixedUpdate();
        }

        float postSpeed = dir * dashSpeed * postDashSpeedMultiplier;
        rb.velocity = new Vector2(postSpeed, postDashUpwardBoost);
        rb.gravityScale = originalGravityScale * postDashGravityMultiplier;
        isPostDashHang = true;

        float hangEnd = Time.time + postDashHangDuration;
        while (Time.time < hangEnd)
        {
            if (isGrounded) break;
            rb.velocity = new Vector2(postSpeed, rb.velocity.y);
            yield return new WaitForFixedUpdate();
        }

        if (!isGrounded)
        {
            float decelStart = Time.time;
            float decelEnd = decelStart + postDashDecelDuration;
            float startSpeed = rb.velocity.x;
            while (Time.time < decelEnd)
            {
                if (isGrounded) break;
                float t = (Time.time - decelStart) / postDashDecelDuration;
                float curSpeed = Mathf.Lerp(startSpeed, 0f, t);
                rb.velocity = new Vector2(curSpeed, rb.velocity.y);
                yield return new WaitForFixedUpdate();
            }

            if (!isGrounded) rb.velocity = new Vector2(0f, rb.velocity.y);
        }

        rb.gravityScale = originalGravityScale;
        isPostDashHang = false;
        isDashing = false;
    }

    private float DetermineDashDirection()
    {
        if (Mathf.Abs(xAxis) > 0.01f) return Mathf.Sign(xAxis);
        if (Mathf.Abs(rb.velocity.x) > 0.01f) return Mathf.Sign(rb.velocity.x);
        return lastMoveDir;
    }

    // ---- Fast fall & ground check ----
    private void FastFallCheck()
    {
        if (isDashing || isPostDashHang) return;
        if (!isGrounded && (Input.GetKey(KeyCode.F) || Input.GetKey(KeyCode.DownArrow)))
        {
            rb.AddForce(Vector2.down * fastFallForce, ForceMode2D.Force);
        }
    }

    private void CheckGrounded()
    {
        if (groundCheck == null)
        {
            isGrounded = false;
            wasGrounded = false;
            return;
        }

        bool groundedNow = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        if (groundedNow && !wasGrounded)
        {
            jumpsLeft = maxJumps;
            if (anim != null) anim.SetBool("jumping", false);
        }

        wasGrounded = groundedNow;
        isGrounded = groundedNow;
    }

    // ---- Attack ----
    private void Attack()
    {
        if (isDashing || isPostDashHang) return;

        lastAttackTime = Time.time;
        StartCoroutine(AttackRoutine());

        Vector2 origin = (rb != null) ? rb.position : (Vector2)transform.position;
        Vector2 dir = DetermineAttackDirection();

        float range = GetRangeForDir(dir);
        Vector2 boxSize = GetBoxSizeForDir(dir);
        Vector2 boxCenter = origin + dir * range;

        Collider2D[] hits = Physics2D.OverlapBoxAll(boxCenter, boxSize, 0f, attackLayer);
        foreach (var c in hits)
        {
            c.transform.SendMessage("TakeDamage", attackDamage, SendMessageOptions.DontRequireReceiver);
        }

        actionLockedUntil = Time.time + attackDuration;
    }

    private Vector2 DetermineAttackDirection()
    {
        if (Input.GetKey(KeyCode.W)) return Vector2.up;
        if (Input.GetKey(KeyCode.S)) return Vector2.down;
        if (Input.GetKey(KeyCode.A)) return Vector2.left;
        if (Input.GetKey(KeyCode.D)) return Vector2.right;
        return new Vector2(Mathf.Sign(lastMoveDir), 0f);
    }

    private float GetRangeForDir(Vector2 dir)
    {
        if (dir.x > 0f) return (attackRangeRight > 0f) ? attackRangeRight : attackRange;
        if (dir.x < 0f) return (attackRangeLeft > 0f) ? attackRangeLeft : attackRange;
        if (dir.y > 0f) return (attackRangeUp > 0f) ? attackRangeUp : attackRange;
        if (dir.y < 0f) return (attackRangeDown > 0f) ? attackRangeDown : attackRange;
        return attackRange;
    }

    private Vector2 GetBoxSizeForDir(Vector2 dir)
    {
        if (dir.x > 0f) return (attackBoxSizeRight != Vector2.zero) ? attackBoxSizeRight : attackBoxSize;
        if (dir.x < 0f) return (attackBoxSizeLeft != Vector2.zero) ? attackBoxSizeLeft : attackBoxSize;
        if (dir.y > 0f) return (attackBoxSizeUp != Vector2.zero) ? attackBoxSizeUp : attackBoxSize;
        if (dir.y < 0f) return (attackBoxSizeDown != Vector2.zero) ? attackBoxSizeDown : attackBoxSize;
        return attackBoxSize;
    }

    private IEnumerator AttackRoutine()
    {
        yield return new WaitForSeconds(attackDuration);
    }

    // ---- Sanity API ----
    public int MaxSanity => maxSanity;
    public int CurrentSanity => sanity;

    public void SetSanity(int value)
    {
        sanity = Mathf.Clamp(value, 0, maxSanity);
        UpdateSanityUI();
        PersistSanity();
        OnSanityChanged?.Invoke(sanity, maxSanity);
    }

    public void TakeSanityDamage(int amount)
    {
        if (amount <= 0) return;
        SetSanity(sanity - amount);
        if (sanity <= 0)
        {
            // TODO: handle zero sanity
        }
    }

    public void RestoreSanity(int amount)
    {
        if (amount <= 0) return;
        SetSanity(sanity + amount);
    }

    private void EnsureSanityUI()
    {
        if (sanitySlider != null) return;

        GameObject canvasGO = new GameObject("TempSanityCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // render above overlay
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();
        DontDestroyOnLoad(canvasGO);

        GameObject sliderGO = new GameObject("TempSanityBar");
        sliderGO.transform.SetParent(canvasGO.transform, false);
        sanitySlider = sliderGO.AddComponent<Slider>();

        RectTransform rt = sliderGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(12f, -12f);
        rt.sizeDelta = new Vector2(200f, 20f);

        GameObject bgGO = new GameObject("Background");
        bgGO.transform.SetParent(sliderGO.transform, false);
        Image bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.75f);
        RectTransform bgRt = bgGO.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;

        GameObject fillAreaGO = new GameObject("Fill Area");
        fillAreaGO.transform.SetParent(sliderGO.transform, false);
        RectTransform fillRt = fillAreaGO.AddComponent<RectTransform>();
        fillRt.anchorMin = new Vector2(0f, 0f);
        fillRt.anchorMax = new Vector2(1f, 1f);
        fillRt.offsetMin = new Vector2(4f, 4f);
        fillRt.offsetMax = new Vector2(-4f, -4f);

        GameObject fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(fillAreaGO.transform, false);
        Image fillImg = fillGO.AddComponent<Image>();
        fillImg.color = new Color(0.8f, 0.1f, 0.1f, 1f);
        RectTransform fRt = fillGO.GetComponent<RectTransform>();
        fRt.anchorMin = new Vector2(0f, 0f);
        fRt.anchorMax = new Vector2(1f, 1f);
        fRt.offsetMin = Vector2.zero;
        fRt.offsetMax = Vector2.zero;

        sanitySlider.direction = Slider.Direction.LeftToRight;
        sanitySlider.transition = Selectable.Transition.None;
        sanitySlider.minValue = 0f;
        sanitySlider.maxValue = maxSanity;
        sanitySlider.value = sanity;
        sanitySlider.targetGraphic = fillImg;
        sanitySlider.fillRect = fRt;

        GameObject labelGO = new GameObject("Label");
        labelGO.transform.SetParent(sliderGO.transform, false);
        var label = labelGO.AddComponent<Text>();
        label.text = "Sanity";
        label.color = Color.white;
        label.alignment = TextAnchor.MiddleLeft;
        RectTransform lRt = labelGO.GetComponent<RectTransform>();
        lRt.anchorMin = new Vector2(0f, 0f);
        lRt.anchorMax = new Vector2(0f, 1f);
        lRt.pivot = new Vector2(0f, 0.5f);
        lRt.sizeDelta = new Vector2(60f, 20f);
        lRt.anchoredPosition = new Vector2(-62f, 0f);
    }

    private void UpdateSanityUI()
    {
        if (sanitySlider == null) return;
        sanitySlider.maxValue = maxSanity;
        sanitySlider.value = sanity;
    }

    public void NewGameResetSaves()
    {
        PlayerPrefs.DeleteKey(PrefKeySanity);
        PlayerPrefs.DeleteKey(PrefKeyHubVisited);
        PlayerPrefs.Save();

        sanity = Mathf.Clamp(sanity, 0, maxSanity);
        UpdateSanityUI();
        PersistSanity();
    }

    private void PersistSanity()
    {
        PlayerPrefs.SetInt(PrefKeySanity, sanity);
        PlayerPrefs.Save();
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }

        Vector2 origin = (rb != null) ? rb.position : (Vector2)transform.position;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(origin + Vector2.right * GetRangeForDir(Vector2.right), GetBoxSizeForDir(Vector2.right));
        Gizmos.DrawWireCube(origin + Vector2.left * GetRangeForDir(Vector2.left), GetBoxSizeForDir(Vector2.left));
        Gizmos.DrawWireCube(origin + Vector2.up * GetRangeForDir(Vector2.up), GetBoxSizeForDir(Vector2.up));
        Gizmos.DrawWireCube(origin + Vector2.down * GetRangeForDir(Vector2.down), GetBoxSizeForDir(Vector2.down));

        if (Application.isPlaying)
        {
            Vector2 aimDir = DetermineAttackDirection();
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(origin + aimDir * GetRangeForDir(aimDir), GetBoxSizeForDir(aimDir));
        }
    }
}
