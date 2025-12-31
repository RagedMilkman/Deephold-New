using UnityEngine;

/// <summary>
/// Intent representing the desire to move into and maintain engagement range with a target.
/// </summary>
public sealed class EngageIntent : IIntent
{
    public IntentType Type => IntentType.Engage;
    public float Urgency { get; set; }

    public string TargetId;
    public CharacterKnowledge Target;    
    public EngageTactics Tactics;

    public Vector3 TargetLocationVector => Target.Position.Value.Value.Vector;
    public Transform TargetLocationTransform => Target.Position.Value.Value.Transform;
}

public enum EngageTactic
{
    Pursue
}

[System.Serializable]
public sealed class EngageTactics
{
    public EngageTactic Tactic = EngageTactic.Pursue;
    public PursueTactic Pursue;
}

[System.Serializable]
public sealed class PursueTactic
{
    public float MinDesiredRange;
    public float PreferredDistance;
    public float MaxDesiredRange;
}
