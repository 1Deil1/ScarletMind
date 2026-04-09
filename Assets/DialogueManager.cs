using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.SceneManagement;

public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }

    [Header("UI References � drag these in from the Inspector")]
    public GameObject        dialoguePanel;
    public TextMeshProUGUI   dialogueText;
    [SerializeField] private Button             nextButton;
    [SerializeField] private Button             closeButton;
    [SerializeField] private TextMeshProUGUI    speakerNameText;
    [Tooltip("Drag the PortraitLeft GameObject here.")]
    [SerializeField] private GameObject         leftPortraitObject;
    [Tooltip("Drag the PortraitRight GameObject here.")]
    [SerializeField] private GameObject         rightPortraitObject;

    // Resolved at runtime from the GameObjects above
    private Image leftPortraitImage;
    private Image rightPortraitImage;
    private RawImage leftPortraitRaw;
    private RawImage rightPortraitRaw;

    [Header("Input")]
    [SerializeField] private KeyCode advanceKey = KeyCode.E;
    [SerializeField] private KeyCode closeKey   = KeyCode.Escape;

    [Header("Typewriter")]
    [SerializeField] private float charsPerSecond = 45f;
    [SerializeField] private bool  useTypewriter  = true;

    private DialogueLine[]   lines;
    private int              index;
    private DialogueTrigger  currentTrigger;
    public  bool             IsActive { get; private set; }

    private Coroutine typeCoroutine;
    private bool      typing;
    private string    currentFullLine;

    private static readonly Color kHidden  = new Color(1f, 1f, 1f, 0f);
    private static readonly Color kVisible = new Color(1f, 1f, 1f, 1f);

    // ?? Unity messages ????????????????????????????????????????????

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // If fields were not dragged in, try to find them automatically as fallback
        AutoWireIfNeeded();

        HidePanel();
        WireButtons();
        SetSpeakerName(null);
    }

    private void OnDestroy()
    {
        if (IsActive) PlayerControlls.Instance?.SetInputLocked(false);
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!IsActive) return;
        if (Input.GetKeyDown(advanceKey))
        {
            if (typing) CompleteTypewriter();
            else        DisplayNextSentence();
        }
        if (Input.GetKeyDown(closeKey)) EndDialogue();
    }

    // ?? Auto-wire fallback (only fills slots that are still null) ?

    private void AutoWireIfNeeded()
    {
        // Resolve Image or RawImage from the dragged-in GameObjects first
        if (leftPortraitObject != null && leftPortraitImage == null && leftPortraitRaw == null)
        {
            leftPortraitImage = leftPortraitObject.GetComponent<Image>();
            if (leftPortraitImage == null)
                leftPortraitRaw = leftPortraitObject.GetComponent<RawImage>();
        }

        if (rightPortraitObject != null && rightPortraitImage == null && rightPortraitRaw == null)
        {
            rightPortraitImage = rightPortraitObject.GetComponent<Image>();
            if (rightPortraitImage == null)
                rightPortraitRaw = rightPortraitObject.GetComponent<RawImage>();
        }

        // Find DialoguePanel if not assigned
        if (dialoguePanel == null)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "DialoguePanel") { dialoguePanel = t.gameObject; break; }

            if (dialoguePanel == null)
            {
                foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        if (t.name == "DialoguePanel") { dialoguePanel = t.gameObject; break; }
            }
        }

        if (dialoguePanel == null)
        {
            Debug.LogWarning("[DialogueManager] Could not find 'DialoguePanel'. Drag it into the Inspector.");
            return;
        }

        if (dialogueText == null)
            dialogueText = dialoguePanel.GetComponentInChildren<TextMeshProUGUI>(true);

        foreach (var b in dialoguePanel.GetComponentsInChildren<Button>(true))
        {
            if (nextButton  == null && b.name.Contains("Next"))  nextButton  = b;
            if (closeButton == null && b.name.Contains("Close")) closeButton = b;
        }

        if (speakerNameText == null)
            foreach (var tmp in dialoguePanel.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (tmp.name.Contains("Speaker")) { speakerNameText = tmp; break; }

        // Portrait fallback: search by name if GameObjects were not dragged in
        if (leftPortraitImage == null || rightPortraitImage == null)
            SearchPortraitsIn(dialoguePanel.transform);

        // Search from the Canvas that holds the panel (portraits may be siblings, not children)
        if (leftPortraitImage == null || rightPortraitImage == null)
        {
            Canvas parentCanvas = dialoguePanel.GetComponentInParent<Canvas>();
            if (parentCanvas != null)
                SearchPortraitsIn(parentCanvas.transform);
        }

        // Search from this DialogueManager's own transform
        if (leftPortraitImage == null || rightPortraitImage == null)
            SearchPortraitsIn(transform);

        // Last resort: search all scene root objects
        if (leftPortraitImage == null || rightPortraitImage == null)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                SearchPortraitsIn(root.transform);
                if (leftPortraitImage != null && rightPortraitImage != null) break;
            }
        }

        bool leftOk  = leftPortraitImage  != null || leftPortraitRaw  != null;
        bool rightOk = rightPortraitImage != null || rightPortraitRaw != null;

        if (!leftOk)
            Debug.LogWarning("[DialogueManager] PortraitLeft NOT found. " +
                "Name an Image or RawImage 'PortraitLeft' or drag the GameObject into the Inspector slot.");
        if (!rightOk)
            Debug.LogWarning("[DialogueManager] PortraitRight NOT found. " +
                "Name an Image or RawImage 'PortraitRight' or drag the GameObject into the Inspector slot.");

        Debug.Log($"[DialogueManager] Wired � " +
                  $"panel={dialoguePanel != null} " +
                  $"text={dialogueText != null} " +
                  $"leftPortrait={leftOk} (Image={leftPortraitImage != null} Raw={leftPortraitRaw != null}) " +
                  $"rightPortrait={rightOk} (Image={rightPortraitImage != null} Raw={rightPortraitRaw != null})");
    }

    /// <summary>
    /// Search for portrait objects under root by name, then resolve Image or RawImage.
    /// </summary>
    private void SearchPortraitsIn(Transform root)
    {
        // First pass: search by Image component
        foreach (var img in root.GetComponentsInChildren<Image>(true))
        {
            string n = NormalizeName(img.gameObject.name);
            if (leftPortraitImage == null && IsLeftName(n))
            {
                leftPortraitImage  = img;
                leftPortraitObject = img.gameObject;
            }
            if (rightPortraitImage == null && IsRightName(n))
            {
                rightPortraitImage  = img;
                rightPortraitObject = img.gameObject;
            }
        }

        // Second pass: search by RawImage component (user may have used RawImage instead of Image)
        if (leftPortraitRaw == null || rightPortraitRaw == null)
        {
            foreach (var raw in root.GetComponentsInChildren<RawImage>(true))
            {
                string n = NormalizeName(raw.gameObject.name);
                if (leftPortraitImage == null && leftPortraitRaw == null && IsLeftName(n))
                {
                    leftPortraitRaw    = raw;
                    leftPortraitObject = raw.gameObject;
                }
                if (rightPortraitImage == null && rightPortraitRaw == null && IsRightName(n))
                {
                    rightPortraitRaw    = raw;
                    rightPortraitObject = raw.gameObject;
                }
            }
        }

        // Third pass: search by Transform name (the Image/RawImage may be on a child)
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            string n = NormalizeName(t.name);
            if (leftPortraitImage == null && leftPortraitRaw == null && IsLeftName(n))
            {
                leftPortraitImage = t.GetComponentInChildren<Image>(true);
                if (leftPortraitImage == null) leftPortraitRaw = t.GetComponentInChildren<RawImage>(true);
                if (leftPortraitImage != null || leftPortraitRaw != null)
                    leftPortraitObject = t.gameObject;
            }
            if (rightPortraitImage == null && rightPortraitRaw == null && IsRightName(n))
            {
                rightPortraitImage = t.GetComponentInChildren<Image>(true);
                if (rightPortraitImage == null) rightPortraitRaw = t.GetComponentInChildren<RawImage>(true);
                if (rightPortraitImage != null || rightPortraitRaw != null)
                    rightPortraitObject = t.gameObject;
            }
        }
    }

    private static string NormalizeName(string name)
    {
        return name.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
    }

    private static bool IsLeftName(string n)
    {
        return n.Contains("portraitleft") || n.Contains("leftportrait") ||
               n == "portraitl" || n == "faceleft" || n == "leftface";
    }

    private static bool IsRightName(string n)
    {
        return n.Contains("portraitright") || n.Contains("rightportrait") ||
               n == "portraitr" || n == "faceright" || n == "rightface";
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

    // ?? Panel show / hide ?????????????????????????????????????????

    private void HidePanel()
    {
        if (dialoguePanel != null) dialoguePanel.SetActive(false);
        ApplyPortrait(leftPortraitImage,  leftPortraitRaw,  null);
        ApplyPortrait(rightPortraitImage, rightPortraitRaw, null);
        IsActive        = false;
        typing          = false;
        currentFullLine = null;
    }

    // ?? Public API ????????????????????????????????????????????????

    public void StartDialogue(DialogueLine[] dialogueLines, DialogueTrigger trigger)
    {
        if (dialogueLines == null || dialogueLines.Length == 0) return;

        // Last-chance wire in case Awake ran before the scene was fully loaded
        AutoWireIfNeeded();
        WireButtons();

        if (dialoguePanel == null || dialogueText == null)
        {
            Debug.LogWarning("[DialogueManager] Cannot start dialogue � UI refs missing. " +
                             "Drag DialoguePanel, DialogueText, PortraitLeft and PortraitRight " +
                             "into the DialogueManager Inspector slots.");
            return;
        }

        HidePanel();

        lines          = dialogueLines;
        index          = 0;
        currentTrigger = trigger;

        dialoguePanel.SetActive(true);
        IsActive = true;

        PlayerControlls.Instance?.SetInputLocked(true);
        ShowCurrentSentence();
    }

    public void DisplayNextSentence()
    {
        if (!IsActive || lines == null) return;
        if (typing) { CompleteTypewriter(); return; }
        index++;
        if (index < lines.Length) ShowCurrentSentence();
        else                      EndDialogue();
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

    // ?? Sentence display ??????????????????????????????????????????

    private void ShowCurrentSentence()
    {
        if (dialogueText == null || lines == null || index < 0 || index >= lines.Length) return;

        var line = lines[index];
        SetSpeakerName(string.IsNullOrWhiteSpace(line.speaker) ? null : line.speaker);
        ApplyPortrait(leftPortraitImage,  leftPortraitRaw,  line.leftPortrait);
        ApplyPortrait(rightPortraitImage, rightPortraitRaw, line.rightPortrait);

        currentFullLine = line.text ?? string.Empty;

        if (useTypewriter && charsPerSecond > 0f)
        {
            if (typeCoroutine != null) StopCoroutine(typeCoroutine);
            typeCoroutine = StartCoroutine(TypeLine(currentFullLine));
        }
        else
        {
            typing            = false;
            dialogueText.text = currentFullLine;
        }
    }

    private IEnumerator TypeLine(string line)
    {
        typing            = true;
        dialogueText.text = string.Empty;
        float delay       = 1f / charsPerSecond;
        foreach (char c in line)
        {
            dialogueText.text += c;
            yield return new WaitForSeconds(delay);
        }
        typing        = false;
        typeCoroutine = null;
    }

    private void CompleteTypewriter()
    {
        typing = false;
        if (typeCoroutine != null) { StopCoroutine(typeCoroutine); typeCoroutine = null; }
        dialogueText.text = currentFullLine;
    }

    // ?? Helpers ???????????????????????????????????????????????????

    private void SetSpeakerName(string name)
    {
        if (speakerNameText == null) return;
        bool show = !string.IsNullOrWhiteSpace(name);
        speakerNameText.gameObject.SetActive(show);
        if (show) speakerNameText.text = name;
    }

    /// <summary>
    /// Apply a portrait sprite to whichever component exists (Image or RawImage).
    /// </summary>
    private static void ApplyPortrait(Image img, RawImage raw, Sprite sprite)
    {
        if (img != null)
        {
            if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
            if (!img.enabled) img.enabled = true;
            img.sprite = sprite;
            img.color  = sprite != null ? kVisible : kHidden;
        }
        else if (raw != null)
        {
            if (!raw.gameObject.activeSelf) raw.gameObject.SetActive(true);
            if (!raw.enabled) raw.enabled = true;
            raw.texture = sprite != null ? sprite.texture : null;
            raw.color   = sprite != null ? kVisible : kHidden;
        }
    }
}