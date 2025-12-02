using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerSpawn : MonoBehaviour
{
    [Tooltip("Identifier used by portals to place the player here (e.g. 'hubEntry', 'houseEntry', 'default').")]
    public string spawnId = "default";

    [Tooltip("Optional: draw a gizmo to visualize spawn point.")]
    public Color gizmoColor = new Color(0.2f, 0.9f, 0.2f, 0.65f);
    public float gizmoRadius = 0.25f;

    private void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        Gizmos.DrawSphere(transform.position, gizmoRadius);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, gizmoRadius + 0.05f);
    }
}
