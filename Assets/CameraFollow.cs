using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);

    [Tooltip("Smaller = tighter (snappier) follow. ~0.1-0.4 is common.")]
    [SerializeField] private float smoothTime = 0.2f;

    [Tooltip("Optional cap on camera speed. Use a large number to disable.")]
    [SerializeField] private float maxSpeed = 100f;

    [Tooltip("Lowest world Y the camera is allowed to go.")]
    [SerializeField] private float minY = -5f;

    // If true, read the player's Rigidbody2D.position (interpolated) instead of transform.position
    [SerializeField] private bool useRigidbodyPosition = true;

    [Header("Adaptive follow")]
    [Tooltip("When the player moves faster than this, the camera will use a tighter (smaller) smooth time to reduce visible lag/jitter.")]
    [SerializeField] private float speedThresholdForTightFollow = 12f;
    [Tooltip("Smoothing time used when player is moving fast (smaller = snappier).")]
    [SerializeField] private float fastSmoothTime = 0.05f;

    // Velocity used internally by SmoothDamp — do not modify from inspector
    private Vector3 currentVelocity;

    void Start()
    {
        if (offset == Vector3.zero)
            offset = new Vector3(0f, 0f, -10f);

        // Ensure camera doesn't start below the allowed minimum
        Vector3 p = transform.position;
        transform.position = new Vector3(p.x, Mathf.Max(p.y, minY), p.z);
    }

    void LateUpdate()
    {
        if (PlayerControlls.Instance == null) return;

        // Prefer reading the Rigidbody2D position (it can be interpolated) to avoid physics/renderer mismatch jitter
        Rigidbody2D playerRb = null;
        if (useRigidbodyPosition)
            playerRb = PlayerControlls.Instance.GetComponent<Rigidbody2D>();

        Vector3 playerPos;
        if (playerRb != null)
            playerPos = new Vector3(playerRb.position.x, playerRb.position.y, PlayerControlls.Instance.transform.position.z);
        else
            playerPos = PlayerControlls.Instance.transform.position;

        // Always follow on X
        float targetX = playerPos.x + offset.x;

        // Desired Y based on player + offset, but clamp so camera never goes below minY.
        float candidateY = playerPos.y + offset.y;
        float targetY = Mathf.Max(candidateY, minY);

        // Keep Z offset relative to player (typically -10 for 2D)
        float targetZ = playerPos.z + offset.z;

        Vector3 targetPos = new Vector3(targetX, targetY, targetZ);

        // Adapt smoothing when player is moving fast to reduce trailing/jitter artifacts
        float currentSmooth = smoothTime;
        if (playerRb != null && playerRb.velocity.magnitude > speedThresholdForTightFollow)
        {
            currentSmooth = fastSmoothTime;
        }

        // Use SmoothDamp with explicit delta time for consistent behaviour across frame rates
        transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref currentVelocity, currentSmooth, maxSpeed, Time.deltaTime);
    }
}
