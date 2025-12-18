using UnityEngine;

public sealed class EngageIntent : IIntent
{
    public IntentType Type => IntentType.Engage;
    public float Urgency { get; set; }

    public int TargetId;
    public Vector3 TargetPos;
    public float DesiredRange;
    public bool UseCover;
}
