using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Collider2D))]
public class ScenePortal : MonoBehaviour
{
    [Header("Single target (fallback if Hub<->House is disabled)")]
    [Tooltip("Exact scene name as listed in Build Settings")]
    [SerializeField] private string sceneName = "House";

    [Header("Bidirectional Hub <-> House")]
    [Tooltip("Enable to automatically teleport Hub<->House depending on the current scene")]
    [SerializeField] private bool useHubHousePair = true;
    [Tooltip("Exact name of the Hub scene (must match Build Settings)")]
    [SerializeField] private string hubSceneName = "Hub";
    [Tooltip("Exact name of the House scene (must match Build Settings)")]
    [SerializeField] private string houseSceneName = "House";

    [Header("Spawn Ids")]
    [Tooltip("Spawn id used when destination is the Hub scene")]
    [SerializeField] private string hubSpawnId = "hubEntry";
    [Tooltip("Spawn id used when destination is the House scene")]
    [SerializeField] private string houseSpawnId = "houseEntry";
    [Tooltip("Fallback spawn id when using single target mode")]
    [SerializeField] private string defaultDestinationSpawnId = "default";

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
        string destination = GetDestinationScene();
        if (string.IsNullOrEmpty(destination)) return;

        if (!string.IsNullOrEmpty(playerTag))
        {
            if (!other.CompareTag(playerTag)) return;
        }
        else
        {
            if (other.GetComponent<PlayerControlls>() == null) return;
        }

        // Decide spawn id for destination scene
        string spawnId = GetDestinationSpawnId(destination);
        SceneSpawnState.NextSpawnId = spawnId;

        StartCoroutine(LoadSceneAfterDelay(destination));
    }

    private string GetDestinationScene()
    {
        if (useHubHousePair)
        {
            string current = SceneManager.GetActiveScene().name;
            if (string.Equals(current, hubSceneName))
                return houseSceneName;
            if (string.Equals(current, houseSceneName))
                return hubSceneName;
        }
        return sceneName;
    }

    private string GetDestinationSpawnId(string destinationScene)
    {
        if (useHubHousePair)
        {
            if (destinationScene == hubSceneName) return hubSpawnId;
            if (destinationScene == houseSceneName) return houseSpawnId;
        }
        return defaultDestinationSpawnId;
    }

    private IEnumerator LoadSceneAfterDelay(string destination)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        SceneManager.LoadScene(destination);
    }
}
