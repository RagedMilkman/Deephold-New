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

    public Observation(Transform location, GameObject observedObject)
    {
        Location = location;
        ObservedObject = observedObject;
        Type = ObservationType.Object;
        EventType = ObservationEventType.Nothing;
    }

    public Observation(Transform location, ObservationEventType eventType)
    {
        Location = location;
        EventType = eventType;
        Type = ObservationType.Event;
        ObservedObject = null;
    }
}
