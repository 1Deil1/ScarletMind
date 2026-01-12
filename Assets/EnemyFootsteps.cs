using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class EnemyFootsteps : MonoBehaviour
{
    [Header("Clips")]
    [SerializeField] private AudioClip[] stepClips;

    [Header("Timing")]
    [SerializeField] private float baseInterval = 0.45f;
    [SerializeField] private float minInterval = 0.20f;
    [SerializeField] private float speedForMinInterval = 6f;

    [Header("Gating")]
    [SerializeField] private float moveThreshold = 0.1f;
    [SerializeField] private float volume = 0.8f;
    [SerializeField] private Vector2 pitchRange = new Vector2(0.95f, 1.05f);

    private Rigidbody2D rb;
    private float nextStepTime = 0f;

    private void Awake() { rb = GetComponent<Rigidbody2D>(); }

    private void Update()
    {
        if (stepClips == null || stepClips.Length == 0) return;

        float vx = Mathf.Abs(rb.velocity.x);
        if (vx <= moveThreshold) { nextStepTime = Mathf.Max(nextStepTime, Time.time + 0.05f); return; }

        float t = Mathf.InverseLerp(0f, speedForMinInterval, vx);
        float interval = Mathf.Lerp(baseInterval, minInterval, t);

        if (Time.time >= nextStepTime)
        {
            var clip = stepClips[Random.Range(0, stepClips.Length)];
            float pitch = Random.Range(pitchRange.x, pitchRange.y);
            AudioManager.PlaySfxAt(clip, transform.position, volume, pitch);
            nextStepTime = Time.time + interval;
        }
    }
}
