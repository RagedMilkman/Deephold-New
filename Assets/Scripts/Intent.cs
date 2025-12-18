using UnityEngine;

public enum IntentType
{
    Engage,
    Flee
}

public interface IIntent
{
    IntentType Type { get; }
    float Urgency { get; }
}

public sealed class EngageIntent : IIntent
{
    public IntentType Type => IntentType.Engage;
    public float Urgency { get; init; }

    public int TargetId;
    public Vector3 TargetPos;
    public float DesiredRange;
    public bool UseCover;
}

public sealed class FleeIntent : IIntent
{
    public IntentType Type => IntentType.Flee;
    public float Urgency { get; init; }

    public Vector3 EscapePos;
}
