using UnityEngine;

public class CharacterKnowledge
{
    public string Id { get; }
    public GameObject CharacterObject { get; }

    public Belief<Vector3>? Position { get; private set; }
    public Belief<float>? Health { get; private set; }
    public Belief<GameObject>? Equipped { get; private set; }

    public CharacterKnowledge(string id, GameObject characterObject)
    {
        Id = id;
        CharacterObject = characterObject;
    }

    public void UpdateFromObservation(Observation observation)
    {
        if (observation == null || observation.Type != ObservationType.Character)
            return;

        var subject = InferSubject(CharacterObject ?? observation.ObservedObject);
        var timestamp = observation.Timestamp;
        var confidence = observation.Confidence;

        if (observation.Location)
        {
            Position = new Belief<Vector3>
            {
                Value = observation.Location.position,
                Confidence = confidence,
                TimeStamp = timestamp,
                DecayPerSecond = 0f
            };
        }

        if (observation.CharacterData.Health.HasValue)
        {
            Health = new Belief<float>
            {
                Value = observation.CharacterData.Health.Value,
                Confidence = confidence,
                TimeStamp = timestamp,
                DecayPerSecond = 0f
            };
        }

        if (observation.CharacterData.Equipped)
        {
            Equipped = new Belief<GameObject>
            {
                Value = observation.CharacterData.Equipped,
                Confidence = confidence,
                TimeStamp = timestamp,
                DecayPerSecond = 0f
            };
        }
    }

    private static BeliefSubject InferSubject(GameObject target)
    {
        if (!target)
            return BeliefSubject.Unknown;

        if (target.CompareTag("Enemy"))
            return BeliefSubject.Enemy;
        if (target.CompareTag("Player") || target.CompareTag("Ally"))
            return BeliefSubject.Ally;

        return BeliefSubject.Unknown;
    }
}
