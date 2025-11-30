using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class TWIN : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("Tag used to identify the player.")]
    [SerializeField] private string playerTag = "Player";

    [Header("Animation")]
    [Tooltip("Animator trigger name to switch from Idle -> Attack")]
    [SerializeField] private string attackTrigger = "Attack";

    [Header("Attack / Knockback")]
    [Tooltip("Seconds to wait after triggering the attack animation before applying knockback")]
    [SerializeField] private float attackDelay = 2f;
    [Tooltip("Distance in world units the player should be knocked back (meters)")]
    [SerializeField] private float knockbackDistance = 5f;
    [Tooltip("Time in seconds the knockback should take to travel the distance")]
    [SerializeField] private float knockbackTravelTime = 0.6f;
    [Tooltip("Optional small upward component added to knockback (world units)")]
    [SerializeField] private float knockbackUpward = 0.5f;
    [Tooltip("Optional damage sent to the player via SendMessage(\"TakeDamage\", int)")]
    [SerializeField] private int damage = 1;

    [Header("Behavior")]
    [Tooltip("If true, enemy triggers only once")]
    [SerializeField] private bool singleUse = true;

    private Animator anim;
    private bool hasTriggered = false;

    private void Reset()
    {
        // ensure collider is trigger for easy setup
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    private void Start()
    {
        anim = GetComponent<Animator>();
        Debug.Log($"[TWIN] Start: name='{gameObject.name}' active={gameObject.activeInHierarchy} componentEnabled={enabled}");
        var col = GetComponent<Collider2D>();
        Debug.Log($"[TWIN] Collider present: {(col != null)} isTrigger={(col!=null?col.isTrigger:false)}");
        Debug.Log($"[TWIN] PlayerTag='{playerTag}' AttackTrigger='{attackTrigger}'");
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Debug.Log($"[TWIN] OnTriggerEnter2D called. Other='{other.name}' tag='{other.tag}' layer={LayerMask.LayerToName(other.gameObject.layer)}");

        if (hasTriggered && singleUse)
        {
            Debug.Log("[TWIN] Ignored: already triggered and singleUse==true");
            return;
        }

        if (!string.IsNullOrEmpty(playerTag))
        {
            if (!other.CompareTag(playerTag))
            {
                Debug.Log($"[TWIN] Ignored: other tag != playerTag ('{playerTag}')");
                return;
            }
        }
        else
        {
            if (other.GetComponent<PlayerControlls>() == null)
            {
                Debug.Log("[TWIN] Ignored: other does not have PlayerControlls component");
                return;
            }
        }

        // sanity: check Rigidbody2D presence on player
        var rb = other.attachedRigidbody;
        Debug.Log($"[TWIN] Player Rigidbody2D check: attachedRigidbody={(rb!=null)}");

        // begin attack sequence
        StartCoroutine(AttackSequence(other.gameObject));
    }

    private IEnumerator AttackSequence(GameObject player)
    {
        hasTriggered = true;

        Debug.Log("[TWIN] AttackSequence started.");

        // fire the attack animation on this enemy (Idle -> Attack)
        if (anim != null && !string.IsNullOrEmpty(attackTrigger))
        {
            Debug.Log($"[TWIN] Setting animator trigger '{attackTrigger}'");
            anim.SetTrigger(attackTrigger);
        }
        else
        {
            Debug.Log("[TWIN] No Animator or attackTrigger empty");
        }

        // wait for the animation to play / build up
        yield return new WaitForSeconds(Mathf.Max(0f, attackDelay));
        Debug.Log($"[TWIN] attackDelay elapsed ({attackDelay}s). Applying knockback/damage.");

        if (player == null)
        {
            Debug.LogWarning("[TWIN] player reference null - aborting knockback.");
            yield break;
        }

        Rigidbody2D rb = player.GetComponent<Rigidbody2D>() ?? player.GetComponentInParent<Rigidbody2D>();
        if (rb == null)
        {
            Debug.LogWarning("[TWIN] Player Rigidbody2D not found - cannot apply knockback");
            yield break;
        }

        // disable player controls if PlayerControlls exists
        var pc = player.GetComponent<PlayerControlls>();
        if (pc != null)
        {
            pc.enabled = false;
            Debug.Log("[TWIN] PlayerControlls disabled for knockback");
        }

        // compute required horizontal speed to traverse knockbackDistance in knockbackTravelTime
        float horizontalSpeed = 0f;
        if (knockbackTravelTime > 0f)
            horizontalSpeed = knockbackDistance / knockbackTravelTime;
        else
            horizontalSpeed = knockbackDistance * 10f; // fallback

        // apply leftward velocity (negative x) and upward component
        Vector2 knockVel = new Vector2(-Mathf.Abs(horizontalSpeed), knockbackUpward);
        Debug.Log($"[TWIN] Applying direct velocity for knockback: {knockVel} (distance={knockbackDistance} time={knockbackTravelTime})");

        // set velocity directly for a deterministic effect
        rb.velocity = knockVel;

        // wait until player has moved the configured distance or until travel time elapses
        float startX = player.transform.position.x;
        float elapsed = 0f;
        while (elapsed < knockbackTravelTime)
        {
            elapsed += Time.deltaTime;
            // early exit if distance reached
            if (Mathf.Abs(player.transform.position.x - startX) >= knockbackDistance) break;
            yield return null;
        }

        Debug.Log("[TWIN] Knockback travel finished. Sending damage and finishing.");

        // send damage if needed
        player.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);

        // short pause to show result
        yield return new WaitForSeconds(0.15f);

    }

    // optional: allow re-arming the trigger from other code
    public void ResetTrigger()
    {
        hasTriggered = false;
    }
}
