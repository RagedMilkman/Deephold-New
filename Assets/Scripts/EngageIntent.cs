using UnityEngine;

/// <summary>
/// Intent representing the desire to move into and maintain engagement range with a target.
/// </summary>
public sealed class EngageIntent : IIntent
{
    public IntentType Type => IntentType.Engage;
    public float Urgency { get; set; }

    public string TargetId;
    public Vector3 TargetPosition;
    public float DesiredRange;
    public bool UseCover;
}
