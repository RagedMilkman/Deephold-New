using UnityEngine;

public enum BeliefSource
{
    Sight,
    Sound,
    Inference,
    Teammate
}

public enum BeliefSubject
{
    Unknown,
    Ally,
    Enemy,
    Neutral
}

public enum BeliefProposition
{
    Position,
    Event
}

public sealed class Belief
{
    public BeliefSubject Subject { get; }
    public BeliefProposition Proposition { get; }
    public Vector3 Value { get; }
    public float Confidence { get; }
    public BeliefSource Source { get; }
    public GameObject Target { get; }

    public Belief(BeliefSubject subject, BeliefProposition proposition, Vector3 value, float confidence, BeliefSource source, GameObject target)
    {
        Subject = subject;
        Proposition = proposition;
        Value = value;
        Confidence = Mathf.Clamp01(confidence);
        Source = source;
        Target = target;
    }
}
