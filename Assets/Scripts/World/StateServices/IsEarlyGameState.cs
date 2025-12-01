using UnityEngine;

/// <summary>
/// World state that is active during the early phase of a match.
/// </summary>
public class IsEarlyGameState : WorldState
{
    [SerializeField, Min(0f)]
    float durationSeconds = 180f;

    /// <summary>
    /// Duration of the early game phase in seconds.
    /// </summary>
    public float DurationSeconds
    {
        get => durationSeconds;
        set => durationSeconds = Mathf.Max(0f, value);
    }

    public override bool IsActive => ElapsedTime < durationSeconds;
}
