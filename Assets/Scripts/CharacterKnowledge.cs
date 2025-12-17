using UnityEngine;

public class CharacterKnowledge
{
    public string Id { get; }
    public GameObject CharacterObject { get; }

    public Belief Position { get; private set; }
    public Belief Health { get; private set; }
    public Belief Equipped { get; private set; }

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
        var source = observation.Source;

        if (observation.Location)
        {
            Position = new Belief(
                subject,
                BeliefProposition.Position,
                BeliefValue.FromPosition(observation.Location.position),
                confidence,
                source,
                CharacterObject ?? observation.ObservedObject,
                timestamp);
        }

        if (observation.CharacterData.Health.HasValue)
        {
            Health = new Belief(
                subject,
                BeliefProposition.Health,
                BeliefValue.FromHealth(observation.CharacterData.Health.Value),
                confidence,
                source,
                CharacterObject ?? observation.ObservedObject,
                timestamp);
        }

        if (observation.CharacterData.Equipped)
        {
            Equipped = new Belief(
                subject,
                BeliefProposition.Equipped,
                BeliefValue.FromEquipped(observation.CharacterData.Equipped),
                confidence,
                source,
                CharacterObject ?? observation.ObservedObject,
                timestamp);
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
