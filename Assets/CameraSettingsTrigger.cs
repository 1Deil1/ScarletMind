using UnityEngine;

[DisallowMultipleComponent]
public class CameraSettingsTrigger : MonoBehaviour
{
    [Header("Target Camera")]
    [Tooltip("If null, uses Camera.main.GetComponent<CameraFollow>().")]
    [SerializeField] private CameraFollow cameraFollow;

    [Header("Apply On Enter")]
    [Tooltip("Enable to override camera minY.")]
    [SerializeField] private bool setMinY = true;
    [SerializeField] private float minY = -2f;

    [Tooltip("Enable to cap how high the camera can go.")]
    [SerializeField] private bool setUseMaxY = true;
    [SerializeField] private bool useMaxY = true;

    [Tooltip("Highest world Y allowed when following. Only used if useMaxY is true.")]
    [SerializeField] private float maxY = 8f;

    [Header("Restore")]
    [Tooltip("If true, restores previous values when player exits the trigger.")]
    [SerializeField] private bool restoreOnExit = true;

    // backup
    private float prevMinY;
    private bool prevUseMaxY;
    private float prevMaxY;
    private bool hasBackup = false;

    private void Reset()
    {
        var cam = Camera.main;
        if (cam != null) cameraFollow = cam.GetComponent<CameraFollow>();
        // Try to ensure collider is trigger
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        var cam = GetCameraFollow();
        if (cam == null) return;

        if (restoreOnExit && !hasBackup)
        {
            prevMinY    = cam.GetMinY();
            prevUseMaxY = useMaxY;
            prevMaxY    = maxY;
            hasBackup   = true;
        }

        if (setMinY)    cam.SetMinY(minY);
        if (setUseMaxY) cam.SetMaxYEnabled(useMaxY);
        if (useMaxY)    cam.SetMaxY(maxY);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        if (!restoreOnExit) return;

        var cam = GetCameraFollow();
        if (cam == null) return;

        if (hasBackup)
        {
            cam.SetMinY(prevMinY);
            cam.SetMaxYEnabled(prevUseMaxY);
            cam.SetMaxY(prevMaxY);
            hasBackup = false;
        }
    }

    private CameraFollow GetCameraFollow()
    {
        if (cameraFollow != null) return cameraFollow;
        var cam = Camera.main;
        return cam != null ? cam.GetComponent<CameraFollow>() : null;
    }
}
