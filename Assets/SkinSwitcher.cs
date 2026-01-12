using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class SkinSwitcher : MonoBehaviour
{
    [Header("Skin Roots (child objects under player)")]
    [Tooltip("Child object holding SpriteRenderer+Animator for House skin (full animations).")]
    [SerializeField] private GameObject houseSkinRoot;
    [Tooltip("Child object holding SpriteRenderer+Animator for Hub skin (idle+walk only).")]
    [SerializeField] private GameObject hubSkinRoot;

    [Header("Scenes using Hub skin")]
    [Tooltip("Exact scene names as in Build Settings.")]
    [SerializeField] private string[] hubScenes = { "Hub", "Hub_ROOM", "Hub_PSYCH" };

    [Header("Optional feature gating in Hub")]
    [SerializeField] private bool disableAttackInHub = true;
    [SerializeField] private bool disableDashInHub = true;
    [SerializeField] private bool disableSlideInHub = true;

    private PlayerControlls player;

    public static bool HubSkinActive { get; private set; }
    public static bool DisableAttack { get; private set; }
    public static bool DisableDash   { get; private set; }
    public static bool DisableSlide  { get; private set; }
    public static bool DisableJump   { get; private set; } // NEW

    private void Awake()
    {
        player = GetComponent<PlayerControlls>();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        ApplyForActiveScene();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyForActiveScene();
    }

    private void ApplyForActiveScene()
    {
        if (player == null)
        {
            player = GetComponent<PlayerControlls>();
            if (player == null) { Debug.LogWarning("SkinSwitcher: PlayerControlls not found."); return; }
        }

        bool useHub = IsHubScene(SceneManager.GetActiveScene().name);
        HubSkinActive = useHub;

        // Publish feature gates
        DisableAttack = useHub && disableAttackInHub;
        DisableDash   = useHub && disableDashInHub;
        DisableSlide  = useHub && disableSlideInHub;
        DisableJump   = useHub; // gate jump in all Hub scenes

        // You can remove the cooldown hijacking if you prefer pure hard gating:
        // GateFeatures(useHub); // optional: comment out to stop cooldown inflation
        if (disableAttackInHub || disableDashInHub || disableSlideInHub)
            GateFeatures(useHub); // keep if you still want long cooldown outside hard gate

        // Enable one root, disable the other
        if (hubSkinRoot != null) hubSkinRoot.SetActive(useHub);
        if (houseSkinRoot != null) houseSkinRoot.SetActive(!useHub);

        // Rebind player.anim to the active skin's Animator
        Animator activeAnimator = null;
        if (useHub && hubSkinRoot != null) activeAnimator = hubSkinRoot.GetComponentInChildren<Animator>();
        if (!useHub && houseSkinRoot != null) activeAnimator = houseSkinRoot.GetComponentInChildren<Animator>();

        if (activeAnimator == null)
        {
            Debug.LogWarning("SkinSwitcher: Active skin Animator missing.");
        }
        else
        {
            // Point PlayerControlls to the active Animator
            RebindPlayerAnimator(activeAnimator);
        }
    }

    private bool IsHubScene(string sceneName)
    {
        if (hubScenes == null || hubScenes.Length == 0) return false;
        for (int i = 0; i < hubScenes.Length; i++)
            if (hubScenes[i] == sceneName) return true;
        return false;
    }

    private void RebindPlayerAnimator(Animator newAnimator)
    {
        // Clear any previously set one-shot triggers to avoid cross-skin bleed
        if (player != null && player.GetType() != null)
        {
            // Call safe reset method if available
            // PlayerControlls.ReturnToLocomotion clears triggers; also update facing bool immediately.
            player.GetType().GetMethod("ReturnToLocomotion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(player, new object[] { 0.0f });
        }

        // Assign the new animator
        // PlayerControlls.anim is private; we need to assign via reflection (or make it [SerializeField] internal).
        var f = typeof(PlayerControlls).GetField("anim", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (f != null) f.SetValue(player, newAnimator);

        // Update facing immediately so idle/walk show correctly
        var fp = typeof(PlayerControlls).GetField("facingAnimatorParameter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        string facingParam = fp != null ? (string)fp.GetValue(player) : "facingRight";

        var isFacingRightField = typeof(PlayerControlls).GetField("isFacingRight", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        bool facingRight = isFacingRightField != null ? (bool)isFacingRightField.GetValue(player) : true;

        if (!string.IsNullOrEmpty(facingParam) && newAnimator != null)
            newAnimator.SetBool(facingParam, facingRight);
    }

    private void GateFeatures(bool inHub)
    {
        if (player == null) return;

        // Disable features by hijacking inputs at runtime (non-invasive)
        // We’ll set cooldowns high when entering hub; restore on non-hub scenes.

        // Gate attack
        if (disableAttackInHub)
        {
            var attackCooldownField = typeof(PlayerControlls).GetField("attackCooldown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (attackCooldownField != null)
            {
                float original = (float)attackCooldownField.GetValue(player);
                attackCooldownField.SetValue(player, inHub ? 999f : Mathf.Min(original, 0.35f));
            }
        }

        // Gate dash
        if (disableDashInHub)
        {
            var dashCooldownField = typeof(PlayerControlls).GetField("dashCooldown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (dashCooldownField != null)
            {
                float original = (float)dashCooldownField.GetValue(player);
                dashCooldownField.SetValue(player, inHub ? 999f : Mathf.Min(original, 0.25f));
            }
        }

        // Gate slide
        if (disableSlideInHub)
        {
            var slideCooldownField = typeof(PlayerControlls).GetField("slideCooldown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (slideCooldownField != null)
            {
                float original = (float)slideCooldownField.GetValue(player);
                slideCooldownField.SetValue(player, inHub ? 999f : Mathf.Min(original, 0.4f));
            }
        }
    }
}
