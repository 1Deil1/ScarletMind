using System.Collections;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public static CameraFollow Instance { get; private set; }

    [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);

    [Tooltip("Smaller = tighter (snappier) follow. ~0.1-0.4 is common.")]
    [SerializeField] private float smoothTime = 0.2f;

    [Tooltip("Optional cap on camera speed. Use a large number to disable.")]
    [SerializeField] private float maxSpeed = 100f;

    [Header("Zoom")]
    [Tooltip("Base orthographic size used when zoomMultiplier = 1. Smaller size = closer camera.")]
    [SerializeField] private float baseOrthographicSize = 5f;
    [Tooltip("1 = normal. Higher = more zoomed in. Example: 2 = twice as close.")]
    [SerializeField] private float zoomMultiplier = 1f;
    [Tooltip("How fast the camera zoom changes.")]
    [SerializeField] private float zoomSmoothTime = 0.2f;
    [SerializeField] private float minOrthographicSize = 2f;
    [SerializeField] private float maxOrthographicSize = 12f;

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
    [SerializeField] private Collider2D cameraBounds;
    [SerializeField] private Collider2D[] cameraBoundsList;
    [Tooltip("Extra margin from bounds edges the camera will keep.")]
    [SerializeField] private Vector2 edgePadding = new Vector2(0.5f, 0.5f);
    [SerializeField] private bool clampYToBounds = false;

    [Header("Bounds transition")]
    [Tooltip("Extra tolerance before switching to a different bounds collider. Helps prevent rapid seam switching.")]
    [SerializeField] private float boundsSwitchPadding = 1.0f;
    [Tooltip("How long it takes to blend from one bounds area to another.")]
    [SerializeField] private float boundsBlendTime = 0.35f;

    [Header("Edge behavior (horizontal)")]
    [Tooltip("Width of the soft zone near left/right bounds where the camera gradually reduces centering to avoid showing void.")]
    [SerializeField] private float softEdgeWidth = 2.0f;
    [Tooltip("Deadzone radius: within this horizontal distance the camera stops re-centering aggressively (helps at exact edge).")]
    [SerializeField] private float deadzoneX = 0.75f;
    [Tooltip("Bias towards the interior when near edges. 0 = no bias, 1 = strong bias (camera stays further from the edge).")]
    [Range(0f, 1f)][SerializeField] private float interiorBias = 0.5f;

    [Header("Camera Lock")]
    [Tooltip("If true, camera stops following the player and holds position.")]
    [SerializeField] private bool cameraLocked = false;
    [Tooltip("Optional fixed position to use while locked. Leave null to hold current position when locking.")]
    [SerializeField] private Transform lockPosition;

    [Header("Camera Shake")]
    [SerializeField] private float defaultShakeMagnitude = 0.1f;
    [SerializeField] private float defaultShakeDuration = 0.2f;

    private Vector3 currentVelocity;
    private Vector3 heldLockPosition;

    private Bounds currentBounds;
    private Bounds previousBounds;
    private Bounds targetBounds;
    private bool hasCurrentBounds;
    private float boundsBlendT = 1f;

    private Camera cachedCamera;
    private float zoomVelocity;

    private Vector3 shakeOffset;
    private Coroutine shakeRoutine;

    void Start()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        cachedCamera = GetComponent<Camera>();
        if (cachedCamera != null && cachedCamera.orthographic)
        {
            if (baseOrthographicSize <= 0f)
                baseOrthographicSize = cachedCamera.orthographicSize;

            cachedCamera.orthographicSize = GetTargetOrthographicSize();
        }

        if (offset == Vector3.zero)
            offset = new Vector3(0f, 0f, -10f);

        Vector3 p = transform.position;
        float startY = Mathf.Max(p.y, minY);
        if (useMaxY) startY = Mathf.Min(startY, maxY);
        transform.position = new Vector3(p.x, startY, p.z);

        heldLockPosition = transform.position;
    }

    void LateUpdate()
    {
        UpdateZoom();

        if (cameraLocked)
        {
            Vector3 target = (lockPosition != null)
                ? new Vector3(lockPosition.position.x, lockPosition.position.y, transform.position.z)
                : new Vector3(heldLockPosition.x, heldLockPosition.y, transform.position.z);

            transform.position = target + shakeOffset;
            return;
        }

        if (PlayerControlls.Instance == null) return;

        Rigidbody2D playerRb = useRigidbodyPosition ? PlayerControlls.Instance.GetComponent<Rigidbody2D>() : null;

        Vector3 playerPos = playerRb != null
            ? new Vector3(playerRb.position.x, playerRb.position.y, PlayerControlls.Instance.transform.position.z)
            : PlayerControlls.Instance.transform.position;

        float desiredX = playerPos.x + offset.x;
        float desiredY = Mathf.Max(playerPos.y + offset.y, minY);
        if (useMaxY) desiredY = Mathf.Min(desiredY, maxY);
        float desiredZ = playerPos.z + offset.z;
        Vector3 desired = new Vector3(desiredX, desiredY, desiredZ);

        Vector3 targetPos = ConfineWithSoftEdges(desired);

        if (useMaxY)
            targetPos.y = Mathf.Min(targetPos.y, maxY);

        float currentSmooth = smoothTime;
        if (playerRb != null && playerRb.velocity.magnitude > speedThresholdForTightFollow)
            currentSmooth = fastSmoothTime;

        transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref currentVelocity, currentSmooth, maxSpeed, Time.deltaTime) + shakeOffset;
    }

    private void UpdateZoom()
    {
        if (cachedCamera == null || !cachedCamera.orthographic) return;

        float targetSize = GetTargetOrthographicSize();
        cachedCamera.orthographicSize = Mathf.SmoothDamp(
            cachedCamera.orthographicSize,
            targetSize,
            ref zoomVelocity,
            zoomSmoothTime);
    }

    private float GetTargetOrthographicSize()
    {
        float safeMultiplier = Mathf.Max(0.01f, zoomMultiplier);
        float targetSize = baseOrthographicSize / safeMultiplier;
        return Mathf.Clamp(targetSize, minOrthographicSize, maxOrthographicSize);
    }

    public void SetCameraLocked(bool locked, Transform optionalLockPosition = null)
    {
        cameraLocked = locked;
        lockPosition = optionalLockPosition;

        if (locked && lockPosition == null)
            heldLockPosition = transform.position;
    }

    public void SetMaxYEnabled(bool enabled) { useMaxY = enabled; }
    public void SetMaxY(float value)         { maxY = value; }
    public void SetMinY(float value)         { minY = value; }
    public float GetMinY()                   { return minY; }

    public void SetZoomMultiplier(float value)
    {
        zoomMultiplier = Mathf.Max(0.01f, value);
    }

    public void SetBaseOrthographicSize(float value)
    {
        baseOrthographicSize = Mathf.Max(0.01f, value);
    }

    public float GetZoomMultiplier()
    {
        return zoomMultiplier;
    }

    public float GetCurrentOrthographicSize()
    {
        return cachedCamera != null ? cachedCamera.orthographicSize : 0f;
    }

    public void TriggerShake(float magnitude = -1f, float duration = -1f)
    {
        float mag = magnitude < 0f ? defaultShakeMagnitude : magnitude;
        float dur = duration  < 0f ? defaultShakeDuration  : duration;

        if (shakeRoutine != null) StopCoroutine(shakeRoutine);
        shakeRoutine = StartCoroutine(ShakeRoutine(mag, dur));
    }

    private IEnumerator ShakeRoutine(float magnitude, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float fade = 1f - (elapsed / duration);
            float x = (Random.value * 2f - 1f) * magnitude * fade;
            float y = (Random.value * 2f - 1f) * magnitude * fade;
            shakeOffset = new Vector3(x, y, 0f);
            yield return null;
        }
        shakeOffset = Vector3.zero;
    }

    private Vector3 ConfineWithSoftEdges(Vector3 desired)
    {
        var cam = cachedCamera != null ? cachedCamera : Camera.main;
        if (cam == null || !cam.orthographic) return desired;

        Collider2D[] list = GetEffectiveBoundsList();
        if (list == null || list.Length == 0) return desired;

        Bounds chosen;
        if (!TryGetBestBounds(list, desired, cam, out chosen))
            return desired;

        UpdateCurrentBounds(chosen, desired, cam);

        Bounds activeBounds = GetBlendedBounds();

        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;

        float minXClamp = activeBounds.min.x + halfWidth + edgePadding.x;
        float maxXClamp = activeBounds.max.x - halfWidth - edgePadding.x;
        float minYClamp = activeBounds.min.y + halfHeight + edgePadding.y;
        float maxYClamp = activeBounds.max.y - halfHeight - edgePadding.y;

        float baseX = ClampWithDeadzone(desired.x, minXClamp, maxXClamp, deadzoneX);

        float baseY;
        if (clampYToBounds)
            baseY = ClampWithDeadzone(desired.y, minYClamp, maxYClamp, 0f);
        else
            baseY = minYClamp <= maxYClamp ? Mathf.Min(Mathf.Max(desired.y, minY), maxYClamp) : Mathf.Max(desired.y, minY);

        if (useMaxY) baseY = Mathf.Min(baseY, maxY);

        float x = ApplySoftEdge(desired.x, baseX, minXClamp, maxXClamp);

        return new Vector3(x, baseY, desired.z);
    }

    private void UpdateCurrentBounds(Bounds chosen, Vector3 desired, Camera cam)
    {
        if (!hasCurrentBounds)
        {
            currentBounds = chosen;
            previousBounds = chosen;
            targetBounds = chosen;
            boundsBlendT = 1f;
            hasCurrentBounds = true;
            return;
        }

        if (BoundsRoughlyEqual(targetBounds, chosen))
        {
            AdvanceBoundsBlend();
            return;
        }

        if (DesiredStillFitsCurrentBounds(desired, currentBounds, cam, boundsSwitchPadding))
        {
            AdvanceBoundsBlend();
            return;
        }

        previousBounds = GetBlendedBounds();
        targetBounds = chosen;
        boundsBlendT = 0f;
        AdvanceBoundsBlend();
    }

    private void AdvanceBoundsBlend()
    {
        if (boundsBlendTime <= 0f)
        {
            boundsBlendT = 1f;
            currentBounds = targetBounds;
            return;
        }

        boundsBlendT = Mathf.Clamp01(boundsBlendT + (Time.deltaTime / boundsBlendTime));
        currentBounds = LerpBounds(previousBounds, targetBounds, boundsBlendT);
    }

    private Bounds GetBlendedBounds()
    {
        return boundsBlendT >= 1f ? targetBounds : currentBounds;
    }

    private static Bounds LerpBounds(Bounds a, Bounds b, float t)
    {
        Vector3 center = Vector3.Lerp(a.center, b.center, t);
        Vector3 size = Vector3.Lerp(a.size, b.size, t);
        return new Bounds(center, size);
    }

    private static bool BoundsRoughlyEqual(Bounds a, Bounds b)
    {
        const float epsilon = 0.01f;
        return Vector3.SqrMagnitude(a.center - b.center) <= epsilon * epsilon
            && Vector3.SqrMagnitude(a.size - b.size) <= epsilon * epsilon;
    }

    private bool DesiredStillFitsCurrentBounds(Vector3 desired, Bounds bounds, Camera cam, float extraPadding)
    {
        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;

        float minXClamp = bounds.min.x + halfWidth + edgePadding.x - extraPadding;
        float maxXClamp = bounds.max.x - halfWidth - edgePadding.x + extraPadding;
        float minYClamp = bounds.min.y + halfHeight + edgePadding.y - extraPadding;
        float maxYClamp = bounds.max.y - halfHeight - edgePadding.y + extraPadding;

        return desired.x >= minXClamp && desired.x <= maxXClamp
            && desired.y >= minYClamp && desired.y <= maxYClamp;
    }

    private float ApplySoftEdge(float desiredX, float clampedX, float minXClamp, float maxXClamp)
    {
        float distLeft = Mathf.Abs(desiredX - minXClamp);
        float distRight = Mathf.Abs(maxXClamp - desiredX);

        float leftInfluence = Mathf.Clamp01(1f - (distLeft / Mathf.Max(softEdgeWidth, 0.0001f)));
        float rightInfluence = Mathf.Clamp01(1f - (distRight / Mathf.Max(softEdgeWidth, 0.0001f)));

        float biasAmount = interiorBias * softEdgeWidth;

        float biasedX = desiredX;
        if (leftInfluence > 0f && leftInfluence >= rightInfluence)
        {
            biasedX = desiredX + biasAmount * leftInfluence;
        }
        else if (rightInfluence > 0f)
        {
            biasedX = desiredX - biasAmount * rightInfluence;
        }

        return Mathf.Clamp(biasedX, minXClamp, maxXClamp);
    }

    private float ClampWithDeadzone(float value, float minClamp, float maxClamp, float deadzone)
    {
        if (minClamp > maxClamp) return (minClamp + maxClamp) * 0.5f;

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

            if (minXClamp > maxXClamp && minYClamp > maxYClamp) continue;

            float clampedX = Mathf.Clamp(desired.x, minXClamp, maxXClamp);

            float lowerY = clampYToBounds ? minYClamp : Mathf.Max(minY, float.MinValue);
            float upperY = clampYToBounds ? maxYClamp : maxYClamp;

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
        var cam = cachedCamera != null ? cachedCamera : Camera.main;
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
