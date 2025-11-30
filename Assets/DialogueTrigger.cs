using UnityEngine;

public class DialogueTrigger : MonoBehaviour
{
    [TextArea(2, 6)]
    public string[] Lines;         // Dialog lines editable in inspector
    public GameObject promptUI;    // assign the InteractPrompt GameObject (can contain a TextMeshProUGUI)

    private bool playerInRange = false;

    private void Start()
    {
        Debug.Log($"[DialogueTrigger] Start on '{gameObject.name}'. promptUI assigned: {promptUI != null}. Lines count: {(Lines != null ? Lines.Length : 0)}");
        if (promptUI != null)
            promptUI.SetActive(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[DialogueTrigger] OnTriggerEnter: collider='{other.name}', tag='{other.tag}' on '{gameObject.name}'");
        if (!other.CompareTag("Player"))
            return;

        Debug.Log($"[DialogueTrigger] Player entered trigger on '{gameObject.name}'");
        playerInRange = true;
        if (promptUI != null)
            promptUI.SetActive(true);
    }

    private void OnTriggerExit(Collider other)
    {
        Debug.Log($"[DialogueTrigger] OnTriggerExit: collider='{other.name}', tag='{other.tag}' on '{gameObject.name}'");
        if (!other.CompareTag("Player"))
            return;

        Debug.Log($"[DialogueTrigger] Player exited trigger on '{gameObject.name}'");
        playerInRange = false;
        if (promptUI != null)
            promptUI.SetActive(false);
    }

    private void Update()
    {
        if (!playerInRange)
            return;

        // If dialog is already active, do nothing
        if (DialogueManager.Instance != null && DialogueManager.Instance.IsActive)
        {
            // still useful to know why input won't start a new dialog
            // Debug message intentionally not spammy
            return;
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            Debug.Log($"[DialogueTrigger] 'E' pressed while in range on '{gameObject.name}'");
            // Hide prompt and start dialog
            if (promptUI != null)
                promptUI.SetActive(false);

            if (DialogueManager.Instance != null)
            {
                DialogueManager.Instance.StartDialogue(Lines, this);
            }
            else
            {
                Debug.LogWarning("[DialogueTrigger] DialogueManager.Instance is null — make sure a DialogueManager exists in the scene.");
            }
        }
    }

    // Called by DialogueManager when the dialog ends
    public void OnDialogueEnded()
    {
        Debug.Log($"[DialogueTrigger] OnDialogueEnded called on '{gameObject.name}'. playerInRange={playerInRange}");
        if (playerInRange && promptUI != null)
            promptUI.SetActive(true);
    }
}