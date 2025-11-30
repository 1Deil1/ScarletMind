using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Collider2D))]
public class ScenePortal : MonoBehaviour
{
    [Tooltip("Exact scene name as listed in Build Settings")]
    [SerializeField] private string sceneName = "Forrest";

    [Tooltip("Optional delay before loading (seconds)")]
    [SerializeField] private float delay = 0f;

    [Tooltip("If set, only objects with this tag will trigger the portal. Leave empty to detect PlayerControlls component.")]
    [SerializeField] private string playerTag = "Player";

    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (string.IsNullOrEmpty(sceneName)) return;

        if (!string.IsNullOrEmpty(playerTag))
        {
            if (!other.CompareTag(playerTag)) return;
        }
        else
        {
            if (other.GetComponent<PlayerControlls>() == null) return;
        }

        StartCoroutine(LoadSceneAfterDelay());
    }

    private IEnumerator LoadSceneAfterDelay()
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        SceneManager.LoadScene(sceneName);
    }
}
