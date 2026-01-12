using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);

    [Tooltip("Smaller = tighter (snappier) follow. ~0.1-0.4 is common.")]
    [SerializeField] private float smoothTime = 0.2f;

    [Tooltip("Optional cap on camera speed. Use a large number to disable.")]
    [SerializeField] private float maxSpeed = 100f;

    [Header("Vertical limits")]
    [Tooltip("Lowest world Y the camera is allowed to go.")]
    [SerializeField] private float minY = -5f;

    [Tooltip("Enable to cap how high the camera can go.")]
    [SerializeField] private bool useMaxY = false;

    [Tooltip("Highest world Y the camera is allowed to go when following.")]
    [SerializeField] private float maxY = 20f;

    [SerializeField] private bool useRigidbodyPosition = true;

    [Header("Adaptive follow")]
    [SerializeField] private float speedThresholdForTightFollow = 12f;
    [SerializeField] private float fastSmoothTime = 0.05f;

    [Header("World bounds confiner")]
    [SerializeField] private Collider2D cameraBounds; // optional single
    [SerializeField] private Collider2D[] cameraBoundsList; // optional multiple
    [Tooltip("Extra margin from bounds edges the camera will keep.")]
    [SerializeField] private Vector2 edgePadding = new Vector2(0.5f, 0.5f);
    [SerializeField] private bool clampYToBounds = false;

    [Header("Edge behavior (horizontal)")]
    [Tooltip("Width of the soft zone near left/right bounds where the camera gradually reduces centering to avoid showing void.")]
    [SerializeField] private float softEdgeWidth = 2.0f;
    [Tooltip("Deadzone radius: within this horizontal distance the camera stops re-centering aggressively (helps at exact edge).")]
    [SerializeField] private float deadzoneX = 0.75f;
    [Tooltip("Bias towards the interior when near edges. 0 = no bias, 1 = strong bias (camera stays further from the edge).")]
    [Range(0f, 1f)] [SerializeField] private float interiorBias = 0.5f;

    [Header("Camera Lock")]
    [Tooltip("If true, camera stops following the player and holds position.")]
    [SerializeField] private bool cameraLocked = false;
    [Tooltip("Optional fixed position to use while locked. Leave null to hold current position when locking.")]
    [SerializeField] private Transform lockPosition;

    private Vector3 currentVelocity;
    private Vector3 heldLockPosition; // cached when locking without a Transform

    void Start()
    {
        if (offset == Vector3.zero)
            offset = new Vector3(0f, 0f, -10f);

        Vector3 p = transform.position;
        float startY = Mathf.Max(p.y, minY);
        if (useMaxY) startY = Mathf.Min(startY, maxY);
        transform.position = new Vector3(p.x, startY, p.z);

        // Initialize held lock position to current camera pos
        heldLockPosition = transform.position;
    }

    void LateUpdate()
    {
        if (cameraLocked)
        {
            // Hold at either the provided lock transform or the last recorded position
            Vector3 target = (lockPosition != null) ? new Vector3(lockPosition.position.x, lockPosition.position.y, transform.position.z)
                                                    : new Vector3(heldLockPosition.x, heldLockPosition.y, transform.position.z);
            transform.position = target;
            return;
        }

        if (PlayerControlls.Instance == null) return;

        Rigidbody2D playerRb = useRigidbodyPosition ? PlayerControlls.Instance.GetComponent<Rigidbody2D>() : null;

        Vector3 playerPos = playerRb != null
            ? new Vector3(playerRb.position.x, playerRb.position.y, PlayerControlls.Instance.transform.position.z)
            : PlayerControlls.Instance.transform.position;

        // Desired follow (before bounds)
        float desiredX = playerPos.x + offset.x;
        float desiredY = Mathf.Max(playerPos.y + offset.y, minY);
        if (useMaxY) desiredY = Mathf.Min(desiredY, maxY); // cap upward motion
        float desiredZ = playerPos.z + offset.z;
        Vector3 desired = new Vector3(desiredX, desiredY, desiredZ);

        // Confine to the best bounds and soften horizontal near edges
        Vector3 targetPos = ConfineWithSoftEdges(desired);

        // Final safety clamp on Y to respect user max (after bounds)
        if (useMaxY)
            targetPos.y = Mathf.Min(targetPos.y, maxY);

        float currentSmooth = smoothTime;
        if (playerRb != null && playerRb.velocity.magnitude > speedThresholdForTightFollow)
            currentSmooth = fastSmoothTime;

        transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref currentVelocity, currentSmooth, maxSpeed, Time.deltaTime);
    }

    // Public API to lock/unlock camera follow
    public void SetCameraLocked(bool locked, Transform optionalLockPosition = null)
    {
        cameraLocked = locked;
        lockPosition = optionalLockPosition;

        if (locked)
        {
            // Cache current position if no explicit lock target
            if (lockPosition == null)
                heldLockPosition = transform.position;
        }
    }

    // Public API to control vertical cap at runtime
    public void SetMaxYEnabled(bool enabled)
    {
        useMaxY = enabled;
    }

    public void SetMaxY(float value)
    {
        maxY = value;
    }

    // Multi-collider confine plus soft edge handling on X to reduce visible void and magnet feel.
    private Vector3 ConfineWithSoftEdges(Vector3 desired)
    {
        var cam = Camera.main;
        if (cam == null || !cam.orthographic) return desired;

        Collider2D[] list = GetEffectiveBoundsList();
        if (list == null || list.Length == 0) return desired;

        // Choose best bounds (least clamping)
        Bounds chosen;
        if (!TryGetBestBounds(list, desired, cam, out chosen))
            return desired;

        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;

        float minXClamp = chosen.min.x + halfWidth + edgePadding.x;
        float maxXClamp = chosen.max.x - halfWidth - edgePadding.x;
        float minYClamp = chosen.min.y + halfHeight + edgePadding.y;
        float maxYClamp = chosen.max.y - halfHeight - edgePadding.y;

        // Base clamp
        float baseX = ClampWithDeadzone(desired.x, minXClamp, maxXClamp, deadzoneX);
        float baseY;
        if (clampYToBounds)
            baseY = ClampWithDeadzone(desired.y, minYClamp, maxYClamp, 0f);
        else
            baseY = minYClamp <= maxYClamp ? Mathf.Min(Mathf.Max(desired.y, minY), maxYClamp) : Mathf.Max(desired.y, minY);

        // Apply user maxY cap after base Y computed (keeps camera from rising too high even inside bounds)
        if (useMaxY) baseY = Mathf.Min(baseY, maxY);

        // Soft edge blend on X: when within softEdgeWidth of either side, bias inward and reduce centering
        float x = ApplySoftEdge(desired.x, baseX, minXClamp, maxXClamp);

        return new Vector3(x, baseY, desired.z);
    }

    private float ApplySoftEdge(float desiredX, float clampedX, float minXClamp, float maxXClamp)
    {
        // Distance to each edge
        float distLeft = Mathf.Abs(desiredX - minXClamp);
        float distRight = Mathf.Abs(maxXClamp - desiredX);

        // Compute influence (0..1) near the closest edge
        float leftInfluence = Mathf.Clamp01(1f - (distLeft / Mathf.Max(softEdgeWidth, 0.0001f)));
        float rightInfluence = Mathf.Clamp01(1f - (distRight / Mathf.Max(softEdgeWidth, 0.0001f)));

        // Bias towards interior: push the target away from the edge slightly
        float biasAmount = interiorBias * softEdgeWidth;

        float biasedX = desiredX;
        if (leftInfluence > 0f && leftInfluence >= rightInfluence)
        {
            // Push interior: move right by bias scaled by influence
            biasedX = desiredX + biasAmount * leftInfluence;
        }
        else if (rightInfluence > 0f)
        {
            // Push interior: move left by bias scaled by influence
            biasedX = desiredX - biasAmount * rightInfluence;
        }

        // Finally, clamp the biased result (and respect deadzone already applied)
        return Mathf.Clamp(biasedX, minXClamp, maxXClamp);
    }

    private float ClampWithDeadzone(float value, float minClamp, float maxClamp, float deadzone)
    {
        if (minClamp > maxClamp) return (minClamp + maxClamp) * 0.5f; // degenerate small bounds

        // If value is near clamps (within deadzone), snap to clamp to avoid jittery recentering,
        // but SmoothDamp will ease into it.
        if (deadzone > 0f)
        {
            if (value < minClamp + deadzone) return minClamp;
            if (value > maxClamp - deadzone) return maxClamp;
        }
        return Mathf.Clamp(value, minClamp, maxClamp);
    }

    private bool TryGetBestBounds(Collider2D[] list, Vector3 desired, Camera cam, out Bounds best)
    {
        best = new Bounds();
        bool anyValid = false;
        float bestSqrDist = float.PositiveInfinity;

        foreach (var c in list)
        {
            if (c == null || !c.enabled) continue;

            Bounds b = c.bounds;
            float halfHeight = cam.orthographicSize;
            float halfWidth = halfHeight * cam.aspect;

            float minXClamp = b.min.x + halfWidth + edgePadding.x;
            float maxXClamp = b.max.x - halfWidth - edgePadding.x;
            float minYClamp = b.min.y + halfHeight + edgePadding.y;
            float maxYClamp = b.max.y - halfHeight - edgePadding.y;

            // Skip completely invalid (tiny) bounds on both axes
            if (minXClamp > maxXClamp && minYClamp > maxYClamp) continue;

            // Compute how much clamping would be needed
            float clampedX = Mathf.Clamp(desired.x, minXClamp, maxXClamp);

            float lowerY = clampYToBounds ? minYClamp : Mathf.Max(minY, float.MinValue);
            float upperY = clampYToBounds ? maxYClamp : maxYClamp;

            // Respect user maxY in scoring too
            if (useMaxY) upperY = Mathf.Min(upperY, maxY);

            float clampedY = Mathf.Clamp(desired.y, lowerY, upperY);

            float sqr = (new Vector2(clampedX, clampedY) - new Vector2(desired.x, desired.y)).sqrMagnitude;
            if (sqr < bestSqrDist)
            {
                bestSqrDist = sqr;
                best = b;
                anyValid = true;
            }
        }

        return anyValid;
    }

    private Collider2D[] GetEffectiveBoundsList()
    {
        if (cameraBoundsList != null && cameraBoundsList.Length > 0)
        {
            if (cameraBounds == null) return cameraBoundsList;

            bool contains = false;
            for (int i = 0; i < cameraBoundsList.Length; i++)
            {
                if (cameraBoundsList[i] == cameraBounds) { contains = true; break; }
            }
            if (contains) return cameraBoundsList;

            var merged = new Collider2D[cameraBoundsList.Length + 1];
            for (int i = 0; i < cameraBoundsList.Length; i++) merged[i] = cameraBoundsList[i];
            merged[merged.Length - 1] = cameraBounds;
            return merged;
        }

        if (cameraBounds != null) return new[] { cameraBounds };
        return null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var cam = Camera.main;
        if (cam == null || !cam.orthographic) return;

        Collider2D[] list = GetEffectiveBoundsList();
        if (list == null) return;

        foreach (var c in list)
        {
            if (c == null) continue;

            Bounds b = c.bounds;
            float halfHeight = cam.orthographicSize;
            float halfWidth = halfHeight * cam.aspect;

            float minXClamp = b.min.x + halfWidth + edgePadding.x;
            float maxXClamp = b.max.x - halfWidth - edgePadding.x;
            float minYClamp = b.min.y + halfHeight + edgePadding.y;
            float maxYClamp = b.max.y - halfHeight - edgePadding.y;

            Gizmos.color = Color.cyan;

            if (minXClamp > maxXClamp || minYClamp > maxYClamp)
            {
                Gizmos.DrawWireSphere(b.center, 0.2f);
                continue;
            }

            Vector3 center = new Vector3((minXClamp + maxXClamp) * 0.5f, (minYClamp + maxYClamp) * 0.5f, 0f);
            Vector3 size = new Vector3(maxXClamp - minXClamp, maxYClamp - minYClamp, 0f);
            Gizmos.DrawWireCube(center, size);
        }
    }
#endif
}
