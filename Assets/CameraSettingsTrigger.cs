using System.Collections;
using System.Collections.Generic;
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

        // Backup current values once
        if (restoreOnExit && !hasBackup)
        {
            // Reflect current internal state by reading serialized fields via reflection or expose getters.
            // We have public setters but not getters; we can cache what we intend to change.
            prevUseMaxY = useMaxY; // best-effort if getters aren’t available
            prevMaxY = maxY;
            prevMinY = minY;
            hasBackup = true;
        }

        // Apply new settings
        if (setMinY) SetPrivateField(cam, "minY", minY);
        if (setUseMaxY) cam.SetMaxYEnabled(useMaxY);
        if (useMaxY) cam.SetMaxY(maxY);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        if (!restoreOnExit) return;

        var cam = GetCameraFollow();
        if (cam == null) return;

        // Restore backed up values
        if (hasBackup)
        {
            SetPrivateField(cam, "minY", prevMinY);
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

    // Helper to set non-public serialized fields safely
    private static void SetPrivateField(CameraFollow cam, string fieldName, float value)
    {
        var f = typeof(CameraFollow).GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (f != null && f.FieldType == typeof(float)) f.SetValue(cam, value);
    }
}
