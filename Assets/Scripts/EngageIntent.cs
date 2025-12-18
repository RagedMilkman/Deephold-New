using UnityEngine;

public sealed class EngageIntent : IIntent
{
    public IntentType Type => IntentType.Engage;
    public float Urgency { get; init; }

    public int TargetId;
    public Vector3 TargetPos;
    public float DesiredRange;
    public bool UseCover;
}
