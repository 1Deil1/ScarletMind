using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[System.Serializable]
public class SceneDialogueConfig
{
    public string sceneName;
    public DialogueLine[] lines;
    public bool onlyFirstTime = true;
    public string playerPrefKey = "";
    public float startDelay = 0.15f;
}

public class HubIntroDialogue : MonoBehaviour
{
    [Header("Scenes to auto-start dialogue in")]
    [SerializeField] private SceneDialogueConfig[] scenes;

    [Header("Manager wait")]
    [SerializeField] private int waitFramesForManager = 180;

    [Header("Testing")]
    [SerializeField] private bool resetAllFlagsOnStart = false;
    [SerializeField] private bool forcePlay = false;

    private static HubIntroDialogue _instance;

    private void Awake()
    {
        // Persist across scenes
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (resetAllFlagsOnStart)
        {
            foreach (var cfg in scenes)
            {
                string key = GetKey(cfg);
                PlayerPrefs.DeleteKey(key);
            }
            PlayerPrefs.Save();
        }

        TryStartForActiveScene();
        // Also handle scene changes
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (_instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryStartForActiveScene();
    }

    private void TryStartForActiveScene()
    {
        var active = SceneManager.GetActiveScene().name;
        var cfgForScene = GetConfigForScene(active);
        if (cfgForScene == null) return;

        if (cfgForScene.onlyFirstTime && !forcePlay)
        {
            string key = GetKey(cfgForScene);
            if (PlayerPrefs.GetInt(key, 0) == 1) return;
        }

        StartCoroutine(Begin(cfgForScene));
    }

    private IEnumerator Begin(SceneDialogueConfig cfg)
    {
        if (cfg.startDelay > 0f) yield return new WaitForSeconds(cfg.startDelay);

        int frames = 0;
        while ((DialogueManager.Instance == null) && frames < waitFramesForManager)
        {
            frames++;
            yield return null;
        }

        if (DialogueManager.Instance == null)
        {
            Debug.LogWarning($"[SceneIntroDialogue] DialogueManager not found in '{SceneManager.GetActiveScene().name}'.");
            yield break;
        }

        if (cfg.lines == null || cfg.lines.Length == 0)
        {
            Debug.LogWarning($"[SceneIntroDialogue] No lines assigned for '{SceneManager.GetActiveScene().name}'.");
            yield break;
        }

        DialogueManager.Instance.StartDialogue(cfg.lines, trigger: null);

        if (cfg.onlyFirstTime && !forcePlay)
        {
            PlayerPrefs.SetInt(GetKey(cfg), 1);
            PlayerPrefs.Save();
        }
    }

    private SceneDialogueConfig GetConfigForScene(string sceneName)
    {
        if (scenes == null) return null;
        foreach (var s in scenes)
            if (!string.IsNullOrEmpty(s.sceneName) && s.sceneName == sceneName)
                return s;
        return null;
    }

    private string GetKey(SceneDialogueConfig cfg)
    {
        return !string.IsNullOrEmpty(cfg.playerPrefKey) ? cfg.playerPrefKey : $"INTRO_SHOWN_{cfg.sceneName}";
    }
}
