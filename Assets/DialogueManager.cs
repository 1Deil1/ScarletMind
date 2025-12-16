using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.SceneManagement;

public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }

    [Header("UI References")]
    public GameObject dialoguePanel;
    public TextMeshProUGUI dialogueText;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private TextMeshProUGUI speakerNameText;

    [Header("Input")]
    [SerializeField] private KeyCode advanceKey = KeyCode.E;
    [SerializeField] private KeyCode closeKey = KeyCode.Escape;

    [Header("Typewriter")]
    [SerializeField] private float charsPerSecond = 45f;
    [SerializeField] private bool useTypewriter = true;

    private DialogueLine[] lines;
    private int index;
    private DialogueTrigger currentTrigger;
    public bool IsActive { get; private set; }

    private Coroutine typeCoroutine;
    private bool typing;
    private string currentFullLine;

    private void Awake()
    {
        // Scene-local singleton (no persistence)
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        EnsureUIRefs();
        HidePanel();
        WireButtons();
        SetSpeakerName(null);
    }

    private void OnDestroy()
    {
        // If a scene changes while dialogue is up, restore input
        if (IsActive) PlayerControlls.Instance?.SetInputLocked(false);
        if (Instance == this) Instance = null;
    }

    private void OnValidate()
    {
        EnsureUIRefs();
    }

    private void EnsureUIRefs()
    {
        // First: try children (recommended: keep DialogueCanvas under this object)
        if (!TryWireFromChildren())
        {
            // Fallback: search active scene roots for an object named "DialoguePanel"
            TryWireFromActiveScene();
        }
    }

    private bool TryWireFromChildren()
    {
        bool wired = false;

        if (dialoguePanel == null)
        {
            var panels = GetComponentsInChildren<Transform>(true);
            foreach (var t in panels)
                if (t.name.Equals("DialoguePanel")) { dialoguePanel = t.gameObject; break; }
        }

        if (dialoguePanel != null)
        {
            if (dialogueText == null)
                dialogueText = dialoguePanel.GetComponentInChildren<TextMeshProUGUI>(true);

            if (nextButton == null || closeButton == null || speakerNameText == null)
            {
                var buttons = dialoguePanel.GetComponentsInChildren<Button>(true);
                foreach (var b in buttons)
                {
                    if (b.name.Contains("Next")) nextButton = b;
                    else if (b.name.Contains("Close")) closeButton = b;
                }

                if (speakerNameText == null)
                {
                    var tmps = dialoguePanel.GetComponentsInChildren<TextMeshProUGUI>(true);
                    foreach (var tmp in tmps)
                        if (tmp.name.Contains("Speaker")) { speakerNameText = tmp; break; }
                }
            }

            wired = (dialogueText != null);
        }

        return wired;
    }

    private void TryWireFromActiveScene()
    {
        GameObject foundPanel = null;
        var roots = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (var root in roots)
        {
            var tfs = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in tfs)
            {
                if (t.name.Equals("DialoguePanel")) { foundPanel = t.gameObject; break; }
            }
            if (foundPanel != null) break;
        }

        if (foundPanel == null) return;

        dialoguePanel = foundPanel;
        dialogueText = dialoguePanel.GetComponentInChildren<TextMeshProUGUI>(true);

        nextButton = null;
        closeButton = null;
        speakerNameText = null;

        var buttons = dialoguePanel.GetComponentsInChildren<Button>(true);
        foreach (var b in buttons)
        {
            if (b.name.Contains("Next")) nextButton = b;
            else if (b.name.Contains("Close")) closeButton = b;
        }

        var tmps = dialoguePanel.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var tmp in tmps)
            if (tmp.name.Contains("Speaker")) { speakerNameText = tmp; break; }
    }

    private void WireButtons()
    {
        if (nextButton != null)
        {
            nextButton.onClick.RemoveAllListeners();
            nextButton.onClick.AddListener(DisplayNextSentence);
        }
        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(EndDialogue);
        }
    }

    private void HidePanel()
    {
        if (dialoguePanel != null) dialoguePanel.SetActive(false);
        IsActive = false;
        typing = false;
        currentFullLine = null;
    }

    private void Update()
    {
        if (!IsActive) return;

        if (Input.GetKeyDown(advanceKey))
        {
            if (typing) CompleteTypewriter();
            else DisplayNextSentence();
        }
        if (Input.GetKeyDown(closeKey)) EndDialogue();
    }

    // Start dialogue: now takes DialogueLine[]
    public void StartDialogue(DialogueLine[] dialogueLines, DialogueTrigger trigger)
    {
        if (dialogueLines == null || dialogueLines.Length == 0) return;

        EnsureUIRefs();
        WireButtons();

        // Guard: UI must exist in this scene
        if (dialoguePanel == null || dialogueText == null)
        {
            Debug.LogWarning("[DialogueManager] UI not wired. Place a 'DialoguePanel' in this scene, with TMP text and Next/Close buttons.");
            return;
        }

        HidePanel(); // clean state

        lines = dialogueLines;
        index = 0;
        currentTrigger = trigger;

        dialoguePanel.SetActive(true);
        IsActive = true;

        PlayerControlls.Instance?.SetInputLocked(true);

        ShowCurrentSentence();
    }

    private void ShowCurrentSentence()
    {
        if (dialogueText == null || lines == null || index < 0 || index >= lines.Length) return;

        var line = lines[index];
        SetSpeakerName(string.IsNullOrWhiteSpace(line.speaker) ? null : line.speaker);

        currentFullLine = line.text ?? string.Empty;

        if (useTypewriter && charsPerSecond > 0f)
        {
            if (typeCoroutine != null) StopCoroutine(typeCoroutine);
            typeCoroutine = StartCoroutine(TypeLine(currentFullLine));
        }
        else
        {
            typing = false;
            dialogueText.text = currentFullLine;
        }
    }

    private IEnumerator TypeLine(string line)
    {
        typing = true;
        dialogueText.text = string.Empty;
        float delay = 1f / charsPerSecond;
        foreach (char c in line)
        {
            dialogueText.text += c;
            yield return new WaitForSeconds(delay);
        }
        typing = false;
        typeCoroutine = null;
    }

    private void CompleteTypewriter()
    {
        typing = false;
        if (typeCoroutine != null)
        {
            StopCoroutine(typeCoroutine);
            typeCoroutine = null;
        }
        dialogueText.text = currentFullLine;
    }

    public void DisplayNextSentence()
    {
        if (!IsActive || lines == null) return;

        if (typing) { CompleteTypewriter(); return; }

        index++;
        if (index < lines.Length)
        {
            ShowCurrentSentence();
        }
        else
        {
            EndDialogue();
        }
    }

    public void EndDialogue()
    {
        if (!IsActive) return;

        HidePanel();
        PlayerControlls.Instance?.SetInputLocked(false);
        SetSpeakerName(null);

        if (currentTrigger != null)
        {
            currentTrigger.OnDialogueEnded();
            currentTrigger = null;
        }
    }

    private void SetSpeakerName(string name)
    {
        if (speakerNameText == null) return;
        bool show = !string.IsNullOrWhiteSpace(name);
        speakerNameText.gameObject.SetActive(show);
        if (show) speakerNameText.text = name;
    }
}