using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class StartMenuController : MonoBehaviour
{
    // Call from your Start button OnClick()
    public void OnStartClick()
    {
        SceneManager.LoadScene("Comic");
    }

    // Call from a Quit button OnClick() or wherever you need to exit the game
    public void OnQuitClick()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}

#if UNITY_EDITOR
// Editor-only helper: keep inside #if UNITY_EDITOR so it doesn't compile into builds.
// Alternatively move this file into an Assets/Editor folder.
public static class FindMissingScripts
{
    [MenuItem("Tools/Find Missing Scripts In Scene")]
    public static void FindInScene()
    {
        var results = new List<string>();
        var all = Object.FindObjectsOfType<GameObject>();
        foreach (var go in all)
        {
            var components = go.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                {
                    string path = GetGameObjectPath(go.transform);
                    results.Add(path + " has missing script at index " + i);
                }
            }
        }

        if (results.Count == 0)
        {
            Debug.Log("No missing scripts found in the scene.");
        }
        else
        {
            Debug.Log($"Found {results.Count} missing script(s):");
            foreach (var r in results) Debug.Log(r);
        }
    }

    private static string GetGameObjectPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
#endif