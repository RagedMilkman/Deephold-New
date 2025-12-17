using UnityEngine;

public enum BeliefSource
{
    Sight,
    Hearing,
    Inferred,
    Communication
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
    Health,
    Equipped,
    Event
}

public enum BeliefValueType
{
    None,
    Position,
    Health,
    Equipped,
    Event
}

public readonly struct BeliefValue
{
    public BeliefValueType ValueType { get; }
    public Vector3 Position { get; }
    public float Health { get; }
    public GameObject Equipped { get; }
    public ObservationEventType EventType { get; }

    private BeliefValue(BeliefValueType valueType, Vector3 position, float health, GameObject equipped, ObservationEventType eventType)
    {
        ValueType = valueType;
        Position = position;
        Health = health;
        Equipped = equipped;
        EventType = eventType;
    }

    public static BeliefValue FromPosition(Vector3 position) =>
        new(BeliefValueType.Position, position, 0f, null, ObservationEventType.Nothing);

    public static BeliefValue FromHealth(float health) =>
        new(BeliefValueType.Health, Vector3.zero, health, null, ObservationEventType.Nothing);

    public static BeliefValue FromEquipped(GameObject equipped) =>
        new(BeliefValueType.Equipped, Vector3.zero, 0f, equipped, ObservationEventType.Nothing);

    public static BeliefValue FromEvent(ObservationEventType eventType, Vector3 position) =>
        new(BeliefValueType.Event, position, 0f, null, eventType);

    public static BeliefValue None =>
        new(BeliefValueType.None, Vector3.zero, 0f, null, ObservationEventType.Nothing);
}

public sealed class Belief
{
    public BeliefSubject Subject { get; }
    public BeliefProposition Proposition { get; }
    public BeliefValue Value { get; }
    public float Confidence { get; }
    public float Timestamp { get; }
    public BeliefSource Source { get; }
    public GameObject Target { get; }

    public Belief(BeliefSubject subject, BeliefProposition proposition, BeliefValue value, float confidence, BeliefSource source, GameObject target, float timestamp)
    {
        Subject = subject;
        Proposition = proposition;
        Value = value;
        Confidence = Mathf.Clamp01(confidence);
        Source = source;
        Target = target;
        Timestamp = timestamp;
    }
}
