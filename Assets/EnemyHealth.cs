using System.Collections;
using UnityEngine;

/// <summary>
/// Simple enemy health component that responds to SendMessage("TakeDamage", int).
/// Integrates with the PlayerControlls script by using PlayerControlls.Instance for knockback direction if available.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class EnemyHealth : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private int maxHealth = 5;
    private int currentHealth;

    [Header("Damage / Invulnerability")]
    [Tooltip("Seconds of invulnerability after taking damage")]
    [SerializeField] private float invulnerabilityTime = 0.18f;
    private bool isInvulnerable = false;

    [Header("Knockback")]
    [Tooltip("Impulse force applied away from the player when hit (0 = no knockback)")]
    [SerializeField] private float knockbackForce = 5f;

    [Header("Visual Feedback")]
    [Tooltip("Sprite flashes this color when hit (set alpha to 0 to disable)")]
    [SerializeField] private Color hitColor = new Color(1f, 0.6f, 0.6f, 1f);
    [Tooltip("How long a single flash frame lasts")]
    [SerializeField] private float flashFrame = 0.06f;

    [Header("Death")]
    [Tooltip("Optional time (seconds) before GameObject is destroyed after death")]
    [SerializeField] private float destroyDelay = 0.08f;

    // cached components
    private SpriteRenderer spriteRenderer;
    private Rigidbody2D rb;
    private Collider2D col;
    private Animator animator;

    private void Awake()
    {
        currentHealth = maxHealth;
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        animator = GetComponent<Animator>();
    }

    /// <summary>
    /// Called via SendMessage from the player code: SendMessage("TakeDamage", damage)
    /// </summary>
    /// <param name="damageObj">expected to be an int (damage amount)</param>
    public void TakeDamage(object damageObj)
    {
        if (isInvulnerable) return;

        int damage = 0;
        if (damageObj is int) damage = (int)damageObj;
        else
        {
            // be tolerant of other numeric types
            int.TryParse(damageObj?.ToString(), out damage);
        }

        if (damage <= 0) return;

        currentHealth -= damage;
        StartCoroutine(DamageFeedback());

        ApplyKnockback();

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private IEnumerator DamageFeedback()
    {
        isInvulnerable = true;

        if (spriteRenderer != null && hitColor.a > 0f)
        {
            Color original = spriteRenderer.color;
            spriteRenderer.color = hitColor;
            yield return new WaitForSeconds(flashFrame);
            spriteRenderer.color = original;
        }

        // wait remaining invulnerability time (minus flashFrame already waited)
        float remaining = Mathf.Max(0f, invulnerabilityTime - flashFrame);
        if (remaining > 0f) yield return new WaitForSeconds(remaining);

        isInvulnerable = false;
    }

    private void ApplyKnockback()
    {
        if (knockbackForce <= 0f || rb == null) return;

        Vector2 sourcePos = Vector2.zero;
        if (PlayerControlls.Instance != null)
            sourcePos = PlayerControlls.Instance.transform.position;
        else
        {
            // fallback: try to find an object tagged "Player"
            var player = GameObject.FindWithTag("Player");
            if (player != null) sourcePos = player.transform.position;
            else return; // no reliable source for knockback direction
        }

        Vector2 dir = ((Vector2)transform.position - sourcePos).normalized;
        if (dir.sqrMagnitude < 0.01f) dir = Vector2.up; // arbitrary fallback
        rb.AddForce(dir * knockbackForce, ForceMode2D.Impulse);
    }

    private void Die()
    {
        // disable collisions so the dead body doesn't block or get hit again
        if (col != null) col.enabled = false;

        // trigger death animation if present
        if (animator != null)
        {
            animator.SetTrigger("Die");
        }

        // destroy after short delay (allow animation or effects)
        Destroy(gameObject, destroyDelay);
    }

    // optional debug: draw remaining health above the enemy in the editor (only when selected)
    private void OnDrawGizmosSelected()
    {
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.6f, $"HP: {currentHealth}/{maxHealth}");
#endif
    }
}
