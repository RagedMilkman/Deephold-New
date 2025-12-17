using UnityEngine;

public enum ObservationType
{
    Object,
    Event
}

public enum ObservationEventType
{
    Nothing = 0,
    AllyDeath = 1,
    EnemyDeath = 2
}

public sealed class Observation
{
    public Transform Location { get; }
    public ObservationType Type { get; }
    public GameObject ObservedObject { get; }
    public ObservationEventType EventType { get; }
    public BeliefSource Source { get; }

    public Observation(Transform location, GameObject observedObject, BeliefSource source)
    {
        Location = location;
        ObservedObject = observedObject;
        Type = ObservationType.Object;
        EventType = ObservationEventType.Nothing;
        Source = source;
    }

    public Observation(Transform location, ObservationEventType eventType, BeliefSource source)
    {
        Location = location;
        EventType = eventType;
        Type = ObservationType.Event;
        ObservedObject = null;
        Source = source;
    }
}
