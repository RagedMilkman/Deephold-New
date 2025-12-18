using UnityEngine;

public class CharacterKnowledge
{
    public string Id { get; }
    public GameObject CharacterObject { get; }

    public Belief<Vector3>? Position { get; private set; }
    public Belief<Vector3>? FacingDirection { get; private set; }
    public Belief<float>? Health { get; private set; }
    public Belief<GameObject>? Equipped { get; private set; }
    public Belief<string>? FactionId { get; private set; }
    public Belief<TopDownMotor.Stance>? Stance { get; private set; }

    public CharacterKnowledge(string id, GameObject characterObject)
    {
        Id = id;
        CharacterObject = characterObject;
    }

    public void UpdateFromObservation(Observation observation)
    {
        if (observation == null || observation.Type != ObservationType.Character)
            return;

        var timestamp = observation.Timestamp;
        var confidence = observation.Confidence;

        if (observation.CharacterData.FacingDirection.HasValue)
        {
            var direction = observation.CharacterData.FacingDirection.Value;
            var normalized = direction.sqrMagnitude > 0.0001f ? direction.normalized : direction;
            FacingDirection = new Belief<Vector3>
            {
                Value = normalized,
                Confidence = confidence,
                TimeStamp = timestamp,
                DecayPerSecond = 0f
            };
        }

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

        if (observation.CharacterData.Stance.HasValue)
        {
            Stance = new Belief<TopDownMotor.Stance>
            {
                Value = observation.CharacterData.Stance.Value,
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

        if (!string.IsNullOrWhiteSpace(observation.CharacterData.FactionId))
        {
            FactionId = new Belief<string>
            {
                Value = observation.CharacterData.FactionId,
                Confidence = confidence,
                TimeStamp = timestamp,
                DecayPerSecond = 0f
            };
        }
    }
}
