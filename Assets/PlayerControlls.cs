using System; // for Action
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PlayerControlls : MonoBehaviour
{
    // ---- Components & runtime state ----
    public static PlayerControlls Instance;

    private Rigidbody2D rb;
    private Animator anim;

    // ---- Movement ----
    [Header("Horizontal Movement")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private bool enableRigidbodyInterpolation = true;

    private float xAxis;
    private float lastMoveDir = 1f;
    private bool isFacingRight = true;
    private const float runInputThreshold = 0.01f;

    [Header("Air Movement")]
    [SerializeField] private float airAcceleration = 20f;
    [SerializeField] [Range(0f, 1f)] private float airControlMultiplier = 0.9f;
    [SerializeField] [Range(0f, 1f)] private float airDecelerationMultiplier = 0.25f;
    [SerializeField] private float airTurnMultiplier = 3f;

    [Header("Jump")]
    [SerializeField] private float jumpForce = 10f;
    [SerializeField] [Range(0f, 1f)] private float secondJumpMultiplier = 0.4f;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.1f;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private int maxJumps = 2;

    private int jumpsLeft;
    private bool isGrounded;
    private bool wasGrounded;
    private float originalGravityScale = 1f;

    [Header("Fast Fall")]
    [SerializeField] private float fastFallForce = 30f;

    [Header("Dash")]
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

    private bool isDashing;
    private bool isPostDashHang;
    private float lastDashTime = -999f;
    private float actionLockedUntil;

    [Header("Input Tweaks")]
    [SerializeField] private float inputHoldAfterJump = 0.12f;
    private float inputHoldTimer;
    private float retainedXAxis;

    // ---- Attack ----
    [Header("Attack")]
    [SerializeField] private KeyCode attackKey = KeyCode.J;
    [SerializeField] private float attackCooldown = 0.35f;
    [SerializeField] private float attackDuration = 0.12f;
    [SerializeField] private float attackRange = 0.8f;
    [SerializeField] private Vector2 attackBoxSize = new Vector2(1.0f, 0.6f);
    [SerializeField] private LayerMask attackLayer;
    [SerializeField] private int attackDamage = 1;

    [Header("Per-side Attack Overrides (<=0 uses default, Vector2.zero uses default)")]
    [SerializeField] private float attackRangeRight = 0.8f;
    [SerializeField] private float attackRangeLeft = 0.8f;
    [SerializeField] private float attackRangeUp = 0.8f;
    [SerializeField] private float attackRangeDown = 0.8f;

    [SerializeField] private Vector2 attackBoxSizeRight = new Vector2(1.0f, 0.6f);
    [SerializeField] private Vector2 attackBoxSizeLeft = new Vector2(1.0f, 0.6f);
    [SerializeField] private Vector2 attackBoxSizeUp = new Vector2(1.0f, 0.6f);
    [SerializeField] private Vector2 attackBoxSizeDown = new Vector2(1.0f, 0.6f);

    private float lastAttackTime = -999f;

    [Header("Facing / Animator")]
    [SerializeField] private string facingAnimatorParameter = "facingRight";
    [SerializeField] private bool useSpriteFlip = true;

    [Header("Animator Parameters")]
    [SerializeField] private string attackTriggerParameter = "attack";
    [SerializeField] private string attackRunTriggerParameter = "attackRun";
    // dashTriggerParameter already exists and is used in Dash()
    [SerializeField] private string slideTriggerParameter = "slide";

    [Header("Animator States")]
    [SerializeField] private string locomotionStateName = "Locomotion";
    [SerializeField] private string dashStateName = "Dash"; // set to your dash state's name

    // ---- Sanity ----
    [Header("Sanity")]
    [SerializeField] private int maxSanity = 100;
    [SerializeField] private int sanity = 100;

    [Header("Sanity Rewards")]
    [Tooltip("Sanity gained per damage point dealt to enemies.")]
    [SerializeField] private int sanityPerDamagePoint = 5;

    [Header("Hub Sanity")]
    [SerializeField] private string hubSceneName = "Hub";
    [SerializeField] private int hubFirstEnterSanity = 50;
    [SerializeField] private int hubReturnBonus = 30;

    private const string PrefKeySanity = "PLAYER_SANITY";
    private const string PrefKeyHubVisited = "HUB_VISITED";

    [Tooltip("Optional: assign UI Slider. If null, a temp persistent one is created.")]
    [SerializeField] private Slider sanitySlider;
    private Text sanityValueText;

    [Header("Save / Testing")]
    [Tooltip("If true, clears saved sanity and hub visit flag when Play starts.")]
    [SerializeField] private bool resetSavesOnPlay = false;

    public static Action<int, int> OnSanityChanged; // current, max

    // ---- Slide ----
    [Header("Slide")]
    [SerializeField] private KeyCode slideKey = KeyCode.C;
    [SerializeField] private float slideSpeed = 10f;
    [SerializeField] private float slideDuration = 0.35f;
    [SerializeField] private float slideCooldown = 0.4f;

    // Runtime
    private bool isSliding = false;
    private float lastSlideTime = -999f;

    // ---- Unity lifecycle ----
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (resetSavesOnPlay)
        {
            PlayerPrefs.DeleteKey(PrefKeySanity);
            PlayerPrefs.DeleteKey(PrefKeyHubVisited);
            PlayerPrefs.Save();
        }

        sanity = PlayerPrefs.HasKey(PrefKeySanity)
            ? Mathf.Clamp(PlayerPrefs.GetInt(PrefKeySanity, sanity), 0, maxSanity)
            : Mathf.Clamp(sanity, 0, maxSanity);

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

        isFacingRight = transform.localScale.x >= 0f ? (lastMoveDir >= 0f) : false;
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
        if (inputLocked)
        {
            // Keep ground checks and air params updated so animations remain correct
            CheckGrounded();
            UpdateAirAnimParams();
            return;
        }

        CheckGrounded();
        ReadInputs();
        HandleJumpInput();
        HandleDashInput();
        HandleSlideInput();
        HandleAttackInput();
        UpdateAirAnimParams();
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

        if (scene.name == hubSceneName)
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
        OnSanityChanged?.Invoke(sanity, maxSanity);
    }

    // ---- Scene spawn ----
    private void ApplySceneSpawn()
    {
        string nextId = SceneSpawnState.NextSpawnId;
        if (string.IsNullOrEmpty(nextId)) return;

        var spawns = FindObjectsOfType<PlayerSpawn>();
        var match = spawns.FirstOrDefault(s => s.spawnId == nextId) ?? spawns.FirstOrDefault(s => s.spawnId == "default");
        if (match != null) transform.position = match.transform.position;

        SceneSpawnState.NextSpawnId = null;
    }

    // ---- Inputs ----
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
        bool canDash = !isDashing && Time.time >= lastDashTime + dashCooldown && Time.time >= actionLockedUntil;
        if (canDash && Input.GetKeyDown(dashKey))
            StartCoroutine(Dash());
    }

    private void HandleAttackInput()
    {
        bool canAttack = Time.time >= lastAttackTime + attackCooldown && Time.time >= actionLockedUntil;
        if (canAttack && (Input.GetKeyDown(attackKey) || Input.GetButtonDown("Fire1")))
            Attack();
    }

    private void HandleSlideInput()
    {
        if (!isGrounded) return;
        if (isSliding || isDashing || isPostDashHang) return;
        if (Time.time < lastSlideTime + slideCooldown) return;
        if (Mathf.Abs(xAxis) <= runInputThreshold) return; // must be moving
        if (!Input.GetKeyDown(slideKey)) return;

        StartCoroutine(Slide());
    }

    // ---- Movement ----
    private void Move()
    {
        if (anim != null)
        {
            bool isRunning = !isDashing && !isSliding && !isPostDashHang && isGrounded && Mathf.Abs(xAxis) > runInputThreshold;
            anim.SetBool("running", isRunning);
        }

        if (Mathf.Abs(xAxis) > runInputThreshold)
        {
            bool wantRight = xAxis > 0f;
            if (wantRight != isFacingRight) SetFacing(wantRight);
        }

        // Do not apply normal movement while dashing, sliding, or during post-dash hang mid-air
        if (isDashing || isSliding || (isPostDashHang && !isGrounded)) return;

        float targetX = xAxis * walkSpeed;

        if (isGrounded)
        {
            rb.velocity = new Vector2(targetX, rb.velocity.y);
            return;
        }

        float airTargetX = xAxis * walkSpeed * airControlMultiplier;
        float currentX = rb.velocity.x;
        float accel = (Mathf.Abs(xAxis) > 0.01f) ? airAcceleration : airAcceleration * airDecelerationMultiplier;

        if (Mathf.Abs(xAxis) > 0.01f && currentX * airTargetX < 0f) accel *= airTurnMultiplier;

        float newX = Mathf.MoveTowards(currentX, airTargetX, accel * Time.fixedDeltaTime);
        rb.velocity = new Vector2(newX, rb.velocity.y);
    }

    private void SetFacing(bool faceRight)
    {
        isFacingRight = faceRight;
        if (anim != null && !string.IsNullOrEmpty(facingAnimatorParameter))
            anim.SetBool(facingAnimatorParameter, isFacingRight);
        if (useSpriteFlip) FlipSprite();
    }

    private void FlipSprite()
    {
        Vector3 s = transform.localScale;
        s.x = Mathf.Abs(s.x) * (isFacingRight ? 1f : -1f);
        transform.localScale = s;
    }

    private void Jump()
    {
        if (anim != null) anim.SetBool("jumping", true);

        retainedXAxis = xAxis;
        inputHoldTimer = inputHoldAfterJump;

        float force = (jumpsLeft == maxJumps) ? jumpForce : jumpForce * secondJumpMultiplier;
        rb.velocity = new Vector2(rb.velocity.x, 0f);
        rb.AddForce(Vector2.up * force, ForceMode2D.Impulse);

        lastJumpTime = Time.time;

        if (anim != null && !string.IsNullOrEmpty(jumpUpStateName))
        {
            // start JumpUp immediately to avoid transition delay
            int hash = Animator.StringToHash(jumpUpStateName);
            if (anim.HasState(0, hash))
                anim.Play(hash, 0, 0f);
        }
    }

    private IEnumerator Dash()
    {
        // Play dash anim and smoke right away
        PlayDashAnim();
        PlayDashSmoke();

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

        // Ensure we leave dash visually
        ReturnToLocomotion();

        var smokeTarget = dashSmokeAnimator != null ? dashSmokeAnimator : anim;
        SetAnimatorBoolSafe(smokeTarget, dashSmokeBoolParameter, false);
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
            rb.AddForce(Vector2.down * fastFallForce, ForceMode2D.Force);
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
        if (isDashing || isPostDashHang || isSliding) return;

        lastAttackTime = Time.time;
        StartCoroutine(AttackRoutine());

        // Animator trigger selection
        bool groundedRunning = isGrounded && Mathf.Abs(xAxis) > runInputThreshold;
        if (anim != null)
        {
            if (groundedRunning && !string.IsNullOrEmpty(attackRunTriggerParameter))
                anim.SetTrigger(attackRunTriggerParameter);
            else if (!string.IsNullOrEmpty(attackTriggerParameter))
                anim.SetTrigger(attackTriggerParameter);
        }

        Vector2 origin = (rb != null) ? rb.position : (Vector2)transform.position;
        Vector2 dir = DetermineAttackDirection();

        float range = GetRangeForDir(dir);
        Vector2 boxSize = GetBoxSizeForDir(dir);
        Vector2 boxCenter = origin + dir * range;

        Collider2D[] hits = Physics2D.OverlapBoxAll(boxCenter, boxSize, 0f, attackLayer);

        int totalDamageDealt = 0;
        foreach (var c in hits)
        {
            c.transform.SendMessage("TakeDamage", attackDamage, SendMessageOptions.DontRequireReceiver);
            totalDamageDealt += attackDamage;
        }

        if (totalDamageDealt > 0 && sanityPerDamagePoint > 0)
            RestoreSanity(totalDamageDealt * sanityPerDamagePoint);

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
        // Safety: force back to locomotion in case Animator transitions aren’t configured yet
        ReturnToLocomotion();
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
            // TODO: handle zero sanity (death, game over, etc.)
        }
    }

    public void RestoreSanity(int amount)
    {
        if (amount <= 0) return;
        SetSanity(sanity + amount);
    }

    // ---- Temp sanity UI ----
    private void EnsureSanityUI()
    {
        if (sanitySlider != null) return;

        var canvasGO = new GameObject("TempSanityCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // render above overlay
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();
        DontDestroyOnLoad(canvasGO);

        var sliderGO = new GameObject("TempSanityBar");
        sliderGO.transform.SetParent(canvasGO.transform, false);
        sanitySlider = sliderGO.AddComponent<Slider>();

        var rt = sliderGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(12f, -12f);
        rt.sizeDelta = new Vector2(200f, 20f);

        var bgGO = new GameObject("Background");
        bgGO.transform.SetParent(sliderGO.transform, false);
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.75f);
        var bgRt = bgGO.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;

        var fillAreaGO = new GameObject("Fill Area");
        fillAreaGO.transform.SetParent(sliderGO.transform, false);
        var fillRt = fillAreaGO.AddComponent<RectTransform>();
        fillRt.anchorMin = new Vector2(0f, 0f);
        fillRt.anchorMax = new Vector2(1f, 1f);
        fillRt.offsetMin = new Vector2(4f, 4f);
        fillRt.offsetMax = new Vector2(-4f, -4f);

        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(fillAreaGO.transform, false);
        var fillImg = fillGO.AddComponent<Image>();
        fillImg.color = new Color(0.8f, 0.1f, 0.1f, 1f);
        var fRt = fillGO.GetComponent<RectTransform>();
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

        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(sliderGO.transform, false);
        var label = labelGO.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.fontSize = 14;
        label.text = "Sanity";
        label.color = Color.white;
        label.alignment = TextAnchor.MiddleLeft;
        var lRt = labelGO.GetComponent<RectTransform>();
        lRt.anchorMin = new Vector2(0f, 0f);
        lRt.anchorMax = new Vector2(0f, 1f);
        lRt.pivot = new Vector2(0f, 0.5f);
        lRt.sizeDelta = new Vector2(60f, 20f);
        lRt.anchoredPosition = new Vector2(-62f, 0f);

        var valueGO = new GameObject("Value");
        valueGO.transform.SetParent(sliderGO.transform, false);
        sanityValueText = valueGO.AddComponent<Text>();
        sanityValueText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        sanityValueText.fontSize = 14;
        sanityValueText.text = sanity.ToString();
        sanityValueText.color = Color.white;
        sanityValueText.alignment = TextAnchor.MiddleRight;
        var vRt = valueGO.GetComponent<RectTransform>();
        vRt.anchorMin = new Vector2(1f, 0f);
        vRt.anchorMax = new Vector2(1f, 1f);
        vRt.pivot = new Vector2(1f, 0.5f);
        vRt.sizeDelta = new Vector2(60f, 20f);
        vRt.anchoredPosition = new Vector2(62f, 0f);
    }

    private void UpdateSanityUI()
    {
        if (sanitySlider == null) return;
        sanitySlider.maxValue = maxSanity;
        sanitySlider.value = sanity;

        if (sanityValueText != null)
            sanityValueText.text = sanity.ToString();
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

    // ---- Gizmos ----
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

    // Add next to other Animator Parameters
    [SerializeField] private string slidingBoolParameter = ""; // optional; set to "sliding" if your Animator uses a bool
    [SerializeField] private string jumpUpStateName = "JumpUp"; // optional: name of your upward jump state

    private IEnumerator Slide()
    {
        isSliding = true;
        lastSlideTime = Time.time;

        if (anim != null)
        {
            if (!string.IsNullOrEmpty(slideTriggerParameter))
                anim.SetTrigger(slideTriggerParameter);

            if (!string.IsNullOrEmpty(slidingBoolParameter))
                anim.SetBool(slidingBoolParameter, true);
        }

        float end = Time.time + slideDuration;
        actionLockedUntil = Mathf.Max(actionLockedUntil, end);

        float dir = Mathf.Abs(xAxis) > 0.01f ? Mathf.Sign(xAxis) : lastMoveDir;

        while (Time.time < end)
        {
            if (!isGrounded) break;
            rb.velocity = new Vector2(dir * slideSpeed, rb.velocity.y);
            yield return new WaitForFixedUpdate();
        }

        isSliding = false;

        if (anim != null && !string.IsNullOrEmpty(slidingBoolParameter))
            anim.SetBool(slidingBoolParameter, false);

        // Safety: force back to locomotion in case Animator transitions aren’t configured yet
        ReturnToLocomotion();
    }

    // Helper: return to locomotion and clear one-shot triggers
    private void ReturnToLocomotion(float fade = 0.05f)
    {
        if (anim == null) return;

        if (!string.IsNullOrEmpty(attackTriggerParameter))     anim.ResetTrigger(attackTriggerParameter);
        if (!string.IsNullOrEmpty(attackRunTriggerParameter))  anim.ResetTrigger(attackRunTriggerParameter);
        if (!string.IsNullOrEmpty(dashTriggerParameter))       anim.ResetTrigger(dashTriggerParameter);
        if (!string.IsNullOrEmpty(slideTriggerParameter))      anim.ResetTrigger(slideTriggerParameter);

        if (!string.IsNullOrEmpty(locomotionStateName))
            CrossFadeIfExists(locomotionStateName, fade, 0);
    }

    // Add this helper anywhere in the class (e.g., below PlayDashSmoke)
    private void CrossFadeIfExists(string stateName, float fade, int layer, string fallbackTrigger = null)
    {
        if (anim == null || string.IsNullOrEmpty(stateName)) return;

        int hash = Animator.StringToHash(stateName);
        if (anim.HasState(layer, hash))
        {
            // layer explicitly set to avoid '-1' layer errors
            anim.CrossFadeInFixedTime(hash, fade, layer, 0f);
        }
        else if (!string.IsNullOrEmpty(fallbackTrigger))
        {
            anim.ResetTrigger(fallbackTrigger); // safety
            anim.SetTrigger(fallbackTrigger);
        }
    }

    // Update PlayDashAnim() to use the safe crossfade
    private void PlayDashAnim()
    {
        if (anim == null) return;

        // Clear one-shots that could block
        if (!string.IsNullOrEmpty(attackTriggerParameter))    anim.ResetTrigger(attackTriggerParameter);
        if (!string.IsNullOrEmpty(attackRunTriggerParameter)) anim.ResetTrigger(attackRunTriggerParameter);
        if (!string.IsNullOrEmpty(slideTriggerParameter))     anim.ResetTrigger(slideTriggerParameter);
        if (!string.IsNullOrEmpty(dashTriggerParameter))      anim.ResetTrigger(dashTriggerParameter);

        // Start dash state immediately at time 0 on base layer
        if (!string.IsNullOrEmpty(dashStateName))
        {
            int dashHash = Animator.StringToHash(dashStateName);
            if (anim.HasState(0, dashHash))
            {
                anim.Play(dashHash, 0, 0f);   // no blend, instant start
            }
            else if (!string.IsNullOrEmpty(dashTriggerParameter))
            {
                anim.SetTrigger(dashTriggerParameter);
            }
        }
        else if (!string.IsNullOrEmpty(dashTriggerParameter))
        {
            anim.SetTrigger(dashTriggerParameter);
        }

        anim.speed = 1f;
    }

    // References (add near other serialized fields)
    [Header("Dash FX")]
    [SerializeField] private Animator dashSmokeAnimator; // child Animator (optional). If null, falls back to 'anim'
    [SerializeField] private Transform dashSmokeOrigin;  // optional feet/ground point
    [SerializeField] private string dashSmokeBoolParameter = "dashsmoke";
    [SerializeField] private float dashSmokeOnDuration = 0.15f;

    // Helper: trigger smoke via bool
    private void PlayDashSmoke()
    {
        Animator target = dashSmokeAnimator != null ? dashSmokeAnimator : anim;
        if (target == null || string.IsNullOrEmpty(dashSmokeBoolParameter)) return;

        if (dashSmokeAnimator != null && dashSmokeOrigin != null)
        {
            var t = dashSmokeAnimator.transform;
            t.position = dashSmokeOrigin.position;
            t.rotation = dashSmokeOrigin.rotation;
            var s = t.localScale;
            s.x = Mathf.Abs(s.x) * (isFacingRight ? 1f : -1f);
            t.localScale = s;
        }

        SetAnimatorBoolSafe(target, dashSmokeBoolParameter, true);

        if (dashSmokeOnDuration > 0f)
            StartCoroutine(ResetDashSmokeAfter(dashSmokeOnDuration));
    }

    private IEnumerator ResetDashSmokeAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        Animator target = dashSmokeAnimator != null ? dashSmokeAnimator : anim;
        SetAnimatorBoolSafe(target, dashSmokeBoolParameter, false);
    }

    // Safely set a bool on an Animator only if the parameter exists
    private static void SetAnimatorBoolSafe(Animator a, string param, bool value)
    {
        if (a == null || string.IsNullOrEmpty(param)) return;
        // Check parameter exists
        foreach (var p in a.parameters)
        {
            if (p.name == param && p.type == AnimatorControllerParameterType.Bool)
            {
                a.SetBool(param, value);
                return;
            }
        }
        // Optional: log once per session (comment out if noisy)
        // Debug.LogWarning($"Animator '{a.name}' missing bool parameter '{param}'.");
    }

    // Air anim tuning
    [Header("Air Anim Tuning")]
    [SerializeField] private float fallYVelThreshold = -0.15f;   // must be below this to start falling
    [SerializeField] private float apexBufferTime = 0.08f;        // small grace window after apex before falling
    [SerializeField] private float postJumpGraceTime = 0.10f;     // ignore small negatives right after a jump

    private float lastJumpTime;
    private float lastApexTime;

    // Update air params with hysteresis
    private void UpdateAirAnimParams()
    {
        if (anim == null) return;

        float y = rb != null ? rb.velocity.y : 0f;

        // Detect apex when velocity crosses from positive to non-positive
        // and store a small buffer window to prevent immediate fall switch
        if (y <= 0f && !isGrounded)
        {
            // If we were recently ascending, mark apex time
            if (Time.time - lastJumpTime < 1.0f) // within a second of any jump
                lastApexTime = Time.time;
        }

        anim.SetBool("grounded", isGrounded);
        anim.SetFloat("yVel", y);

        // Control 'jumping' bool lifespan:
        // - True while airborne and until apex buffer expires
        // - Cleared on ground contact (handled in CheckGrounded)
        bool keepJumping =
            !isGrounded &&
            (
                y > 0f ||                                       // clearly ascending
                (Time.time - lastJumpTime) < postJumpGraceTime || // shortly after jump impulse
                (Time.time - lastApexTime) < apexBufferTime       // small grace at apex
            );

        anim.SetBool("jumping", keepJumping);
    }

    // Add field with other runtime state
    private bool inputLocked = false;

    // Public API
    public void SetInputLocked(bool locked)
    {
        inputLocked = locked;
        if (locked)
        {
            // stop horizontal motion
            if (rb != null) rb.velocity = new Vector2(0f, rb.velocity.y);
            if (anim != null) anim.SetBool("running", false);
        }
    }
}
