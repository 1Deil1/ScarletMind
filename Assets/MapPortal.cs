using System.Collections;
using UnityEngine;

/// <summary>
/// In-scene portal that teleports the player between map areas with a
/// smooth fade-to-black transition and an auto-walk entrance animation.
/// 
/// Setup:
///   1. Create an empty GameObject with a BoxCollider2D (Is Trigger = true).
///   2. Attach this script and assign the Destination transform.
///   3. Create a second portal at the destination pointing back (optional).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class MapPortal : MonoBehaviour
{
    [Header("Destination")]
    [Tooltip("The transform the player will be teleported to.")]
    [SerializeField] private Transform destination;

    [Header("Fade Timing")]
    [Tooltip("Duration of the fade-out to black (seconds).")]
    [SerializeField] private float fadeOutDuration = 0.6f;

    [Tooltip("How long the screen stays fully black (seconds).")]
    [SerializeField] private float holdBlackDuration = 1.5f;

    [Tooltip("Duration of the fade-in from black (seconds).")]
    [SerializeField] private float fadeInDuration = 0.8f;

    [Header("Auto-Walk After Teleport")]
    [Tooltip("If true, the player walks a few steps automatically after arriving.")]
    [SerializeField] private bool autoWalkOnArrival = true;

    [Tooltip("Duration of the auto-walk in seconds.")]
    [SerializeField] private float autoWalkDuration = 0.7f;

    [Tooltip("Speed multiplier relative to player's current walk speed (1 = normal).")]
    [Range(0.3f, 1.5f)]
    [SerializeField] private float autoWalkSpeedMultiplier = 0.6f;

    [Tooltip("Walk direction at destination. +1 = right, -1 = left. 0 = same direction player was facing.")]
    [SerializeField] private float autoWalkDirection = 1f;

    [Header("Detection")]
    [Tooltip("Tag used to identify the player. Leave empty to detect PlayerControlls component.")]
    [SerializeField] private string playerTag = "Player";

    [Header("Cooldown")]
    [Tooltip("Seconds before this portal can trigger again (prevents re-trigger from destination portal).")]
    [SerializeField] private float cooldown = 2f;

    private float lastTeleportTime = -999f;
    private bool isTeleporting;

    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isTeleporting) return;
        if (SceneFader.IsFading) return;
        if (Time.time < lastTeleportTime + cooldown) return;
        if (destination == null) return;
        if (!IsPlayer(other)) return;

        StartCoroutine(TeleportSequence(other));
    }

    private bool IsPlayer(Collider2D other)
    {
        if (!string.IsNullOrEmpty(playerTag))
            return other.CompareTag(playerTag);
        return other.GetComponent<PlayerControlls>() != null;
    }

    private IEnumerator TeleportSequence(Collider2D playerCol)
    {
        isTeleporting = true;
        lastTeleportTime = Time.time;

        var player = PlayerControlls.Instance;
        var rb = playerCol.GetComponent<Rigidbody2D>();

        // Lock player input immediately
        if (player != null) player.SetInputLocked(true);
        if (rb != null) rb.velocity = Vector2.zero;

        // Fade out → teleport while black → fade in
        yield return SceneFader.FadeInOut(fadeOutDuration, holdBlackDuration, fadeInDuration, () =>
        {
            // --- Runs while screen is fully black ---

            // Teleport player
            playerCol.transform.position = destination.position;

            // Zero velocity so player doesn't slide
            if (rb != null) rb.velocity = Vector2.zero;

            // Snap camera instantly to avoid the camera lerping from the old position
            if (CameraFollow.Instance != null)
                CameraFollow.Instance.SnapToTarget();
        });

        // --- Screen is now fading in / visible ---

        if (autoWalkOnArrival && player != null)
        {
            yield return StartCoroutine(AutoWalk(player, rb));
        }

        // Unlock player input
        if (player != null) player.SetInputLocked(false);

        isTeleporting = false;
    }

    private IEnumerator AutoWalk(PlayerControlls player, Rigidbody2D rb)
    {
        float dir = autoWalkDirection;

        // If direction is 0, keep whatever direction the player was facing
        if (Mathf.Approximately(dir, 0f))
            dir = player.transform.localScale.x >= 0f ? 1f : -1f;

        float speed = player.CurrentWalkSpeed * autoWalkSpeedMultiplier;
        var anim = player.GetComponent<Animator>();

        // Face the walk direction
        Vector3 scale = player.transform.localScale;
        scale.x = Mathf.Abs(scale.x) * (dir >= 0f ? 1f : -1f);
        player.transform.localScale = scale;

        // Play run animation
        if (anim != null)
        {
            anim.SetBool("running", true);
            anim.SetBool("jumping", false);
            anim.SetBool("grounded", true);
        }

        float elapsed = 0f;
        while (elapsed < autoWalkDuration)
        {
            if (rb != null)
                rb.velocity = new Vector2(dir * speed, rb.velocity.y);

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Stop walk
        if (rb != null)
            rb.velocity = new Vector2(0f, rb.velocity.y);
        if (anim != null)
            anim.SetBool("running", false);
    }
}
