using UnityEngine;

public enum FootstepType { Default, Stone, Wood }

[DisallowMultipleComponent]
public class FootstepSurface : MonoBehaviour
{
    public FootstepType type = FootstepType.Default;
    [Range(0f, 2f)] public float volumeMul = 1f;
}
