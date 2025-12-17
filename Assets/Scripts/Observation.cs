using UnityEngine;

public enum ObservationType
{
    Object,
    Event,
    Character
}

public enum ObservationEventType
{
    Nothing = 0,
    AllyDeath = 1,
    EnemyDeath = 2
}

public readonly struct CharacterObservationData
{
    public string Id { get; }
    public float? Health { get; }
    public GameObject Equipped { get; }

    public CharacterObservationData(string id, float? health, GameObject equipped)
    {
        Id = id;
        Health = health;
        Equipped = equipped;
    }
}

public sealed class Observation
{
    public Transform Location { get; }
    public ObservationType Type { get; }
    public GameObject ObservedObject { get; }
    public ObservationEventType EventType { get; }
    public BeliefSource Source { get; }
    public float Confidence { get; }
    public float Timestamp { get; }
    public CharacterObservationData CharacterData { get; }

    private Observation(Transform location, ObservationType type, GameObject observedObject, ObservationEventType eventType, BeliefSource source, float confidence, float timestamp, CharacterObservationData characterData)
    {
        Location = location;
        Type = type;
        ObservedObject = observedObject;
        EventType = eventType;
        Source = source;
        Confidence = Mathf.Clamp01(confidence);
        Timestamp = timestamp;
        CharacterData = characterData;
    }

    public static Observation ForObject(Transform location, GameObject observedObject, BeliefSource source, float confidence, float timestamp) =>
        new(location, ObservationType.Object, observedObject, ObservationEventType.Nothing, source, confidence, timestamp, default);

    public static Observation ForEvent(Transform location, ObservationEventType eventType, BeliefSource source, float confidence, float timestamp) =>
        new(location, ObservationType.Event, null, eventType, source, confidence, timestamp, default);

    public static Observation ForCharacter(Transform location, GameObject observedObject, string id, float? health, GameObject equipped, BeliefSource source, float confidence, float timestamp) =>
        new(location, ObservationType.Character, observedObject, ObservationEventType.Nothing, source, confidence, timestamp, new CharacterObservationData(id, health, equipped));
}
