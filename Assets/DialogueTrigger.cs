using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public struct DialogueLine
{
    public string speaker;   // e.g., "Hero", "Bear"
    [TextArea(2, 6)] public string text;
}

public class DialogueTrigger : MonoBehaviour
{
    [Header("Dialogue")]
    public DialogueLine[] Lines;
    public GameObject promptUI;

    [Header("Input")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;
    [SerializeField] private KeyCode teleportKey = KeyCode.T; // NEW: key to teleport
    [SerializeField] private Button promptButton;

    [Header("Teleport")]
    [Tooltip("If true, teleport is only allowed after the dialogue ends.")]
    [SerializeField] private bool requireDialogueBeforeTeleport = true;
    [Tooltip("Optional extra prompt UI shown after dialogue to indicate T to enter.")]
    [SerializeField] private GameObject enterPromptUI; // assign a 'Press T to enter' UI (optional)

    [Header("Speaker (default NPC name for simple use)")]
    [SerializeField] private string speakerName = "";
    public string SpeakerName => speakerName;

    [Header("Re-trigger protection")]
    [Tooltip("How long to ignore the interact key after a dialogue ends (seconds).")]
    [SerializeField] private float retriggerCooldown = 0.15f;

    private bool playerInRange = false;
    private bool dialogueCompleted = false; // NEW: track completion
    private ScenePortal portal;             // NEW: cached portal reference

    private float blockInputUntil = 0f;     // NEW: time until which E is ignored

    private void Start()
    {
        Debug.Log($"[DialogueTrigger] Start '{gameObject.name}'. promptUI: {promptUI != null}. Lines: {(Lines != null ? Lines.Length : 0)}");

        if (promptUI != null) promptUI.SetActive(false);
        if (enterPromptUI != null) enterPromptUI.SetActive(false);

        if (promptButton != null)
        {
            promptButton.onClick.RemoveAllListeners();
            promptButton.onClick.AddListener(() => TryStartDialogue());
        }

        portal = GetComponent<ScenePortal>(); // find portal on same object (recommended)
        if (portal == null)
            Debug.LogWarning($"[DialogueTrigger] No ScenePortal found on '{gameObject.name}'. Teleport (T) will be disabled.");
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = true;

        // Show appropriate prompt
        if (!DialogueManagerIsActive())
        {
            if (promptUI != null) promptUI.SetActive(true);
            if (enterPromptUI != null) enterPromptUI.SetActive(dialogueCompleted && portal != null);
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = false;

        if (promptUI != null) promptUI.SetActive(false);
        if (enterPromptUI != null) enterPromptUI.SetActive(false);
    }

    private void Update()
    {
        if (!playerInRange) return;

        // Dialogue in progress? Don't start new or teleport during dialog.
        if (DialogueManagerIsActive()) return;

        // Respect cooldown after dialogue end to prevent immediate restart
        if (Time.time < blockInputUntil) return;

        // E: start dialogue
        if (Input.GetKeyDown(interactKey))
        {
            TryStartDialogue();
            return;
        }

        // T: teleport (if allowed)
        if (Input.GetKeyDown(teleportKey))
        {
            TryTeleport();
        }
    }

    private bool DialogueManagerIsActive() => DialogueManager.Instance != null && DialogueManager.Instance.IsActive;

    private void TryStartDialogue()
    {
        // Block if still within cooldown window
        if (Time.time < blockInputUntil) return;

        if (promptUI != null) promptUI.SetActive(false);
        if (enterPromptUI != null) enterPromptUI.SetActive(false);

        dialogueCompleted = false; // reset on new dialog

        if (DialogueManager.Instance != null)
        {
            DialogueManager.Instance.StartDialogue(Lines, this);
        }
        else
        {
            Debug.LogWarning("[DialogueTrigger] DialogueManager.Instance is null.");
        }
    }

    private void TryTeleport()
    {
        // Must have a portal
        if (portal == null)
        {
            Debug.LogWarning("[DialogueTrigger] Teleport requested but no ScenePortal component is present.");
            return;
        }

        // Respect the requirement
        if (requireDialogueBeforeTeleport && !dialogueCompleted)
        {
            Debug.Log("[DialogueTrigger] Teleport blocked until dialogue is completed.");
            return;
        }

        // Trigger the teleport
        portal.TriggerTeleport();
    }

    // Called by DialogueManager when the dialog ends
    public void OnDialogueEnded()
    {
        dialogueCompleted = true;

        // Start short cooldown to avoid instant re-trigger from the same key press
        blockInputUntil = Time.time + retriggerCooldown;

        // Restore the appropriate prompt(s)
        if (playerInRange)
        {
            if (promptUI != null) promptUI.SetActive(true);
            if (enterPromptUI != null) enterPromptUI.SetActive(portal != null);
        }
    }
}