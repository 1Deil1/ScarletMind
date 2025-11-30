using UnityEngine;
using TMPro;

public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }

    [Header("UI References")]
    public GameObject dialoguePanel;          // assign the DialogPanel GameObject
    public TextMeshProUGUI dialogueText;      // assign the DialogText (TextMeshProUGUI)

    private string[] sentences;
    private int index;
    private DialogueTrigger currentTrigger;
    public bool IsActive { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        Debug.Log($"[DialogueManager] Awake. dialoguePanel assigned: {dialoguePanel != null}. dialogueText assigned: {dialogueText != null}");
        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);

        if (dialoguePanel == null)
            Debug.LogWarning("[DialogueManager] dialoguePanel is not assigned in the Inspector.");
        if (dialogueText == null)
            Debug.LogWarning("[DialogueManager] dialogueText (TextMeshProUGUI) is not assigned in the Inspector.");
    }

    private void Update()
    {
        if (!IsActive)
            return;

        if (Input.GetKeyDown(KeyCode.E))
        {
            Debug.Log("[DialogueManager] E pressed to advance dialogue");
            DisplayNextSentence();
        }
    }

    // Called by DialogueTrigger to start a dialog
    public void StartDialogue(string[] lines, DialogueTrigger trigger)
    {
        Debug.Log($"[DialogueManager] StartDialogue called. lines count: {(lines != null ? lines.Length : 0)}. trigger='{(trigger != null ? trigger.gameObject.name : "null")}'");
        if (lines == null || lines.Length == 0)
            return;

        sentences = lines;
        index = 0;
        currentTrigger = trigger;

        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);

        IsActive = true;
        ShowCurrentSentence();
    }

    private void ShowCurrentSentence()
    {
        if (dialogueText == null || sentences == null || index < 0 || index >= sentences.Length)
        {
            Debug.LogWarning("[DialogueManager] ShowCurrentSentence called but missing data or index out of range.");
            return;
        }

        dialogueText.text = sentences[index];
        Debug.Log($"[DialogueManager] Showing sentence {index}: {sentences[index]}");
    }

    public void DisplayNextSentence()
    {
        if (!IsActive || sentences == null)
            return;

        index++;
        Debug.Log($"[DialogueManager] DisplayNextSentence called. new index = {index}");
        if (index < sentences.Length)
        {
            ShowCurrentSentence();
        }
        else
        {
            EndDialogue();
        }
    }

    private void EndDialogue()
    {
        Debug.Log("[DialogueManager] EndDialogue called.");
        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);

        IsActive = false;

        if (currentTrigger != null)
        {
            currentTrigger.OnDialogueEnded();
            currentTrigger = null;
        }
    }
}