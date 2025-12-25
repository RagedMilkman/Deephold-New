using UnityEngine;

/// <summary>
/// Intent representing the desire to flee from a threat.
/// </summary>
public sealed class FleeIntent : IIntent
{
    public IntentType Type => IntentType.Flee;
    public float Urgency { get; set; }

    public Vector3 EscapeDirection;
    public float EscapeDistance;
}
