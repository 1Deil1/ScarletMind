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
    [SerializeField] private string hubSceneName = "Hub";
    [SerializeField] private string houseSceneName = "House";

    [Header("Spawn Ids")]
    [SerializeField] private string hubSpawnId = "hubEntry";
    [SerializeField] private string houseSpawnId = "houseEntry";
    [SerializeField] private string defaultDestinationSpawnId = "default";

    [Tooltip("Optional delay before loading (seconds)")]
    [SerializeField] private float delay = 0f;

    [Tooltip("If set, only objects with this tag will trigger the portal. Leave empty to detect PlayerControlls component.")]
    [SerializeField] private string playerTag = "Player";

    [Header("Activation")]
    [Tooltip("If true, entering the trigger auto-teleports. Turn OFF when using DialogueTrigger + key press.")]
    [SerializeField] private bool autoTeleportOnEnter = false; // default off so dialogue can control it

    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!autoTeleportOnEnter) return;              // <-- stop auto teleport unless explicitly enabled
        if (!IsPlayer(other)) return;

        TriggerTeleport();
    }

    private bool IsPlayer(Collider2D other)
    {
        if (!string.IsNullOrEmpty(playerTag))
            return other.CompareTag(playerTag);
        return other.GetComponent<PlayerControlls>() != null;
    }

    // Allow manual teleport via code (used by DialogueTrigger)
    public void TriggerTeleport()
    {
        string destination = GetDestinationScene();
        if (string.IsNullOrEmpty(destination)) return;

        string spawnId = GetDestinationSpawnId(destination);
        SceneSpawnState.NextSpawnId = spawnId;

        StartCoroutine(LoadSceneAfterDelay(destination));
    }

    private string GetDestinationScene()
    {
        if (useHubHousePair)
        {
            string current = SceneManager.GetActiveScene().name;
            if (string.Equals(current, hubSceneName))   return houseSceneName;
            if (string.Equals(current, houseSceneName)) return hubSceneName;
        }
        return sceneName;
    }

    private string GetDestinationSpawnId(string destinationScene)
    {
        if (useHubHousePair)
        {
            if (destinationScene == hubSceneName)  return hubSpawnId;
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
