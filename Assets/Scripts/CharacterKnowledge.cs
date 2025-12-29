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

    public bool HasAnyBeliefs => Position.HasValue || FacingDirection.HasValue || Health.HasValue || Equipped.HasValue ||
                                 FactionId.HasValue || Stance.HasValue;

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
                HalfLifeSeconds = 0f
            };
        }

        if (observation.Location)
        {
            Position = new Belief<Vector3>
            {
                Value = observation.Location.position,
                Confidence = confidence,
                TimeStamp = timestamp,
                HalfLifeSeconds = 0f
            };
        }

        if (observation.CharacterData.Stance.HasValue)
        {
            Stance = new Belief<TopDownMotor.Stance>
            {
                Value = observation.CharacterData.Stance.Value,
                Confidence = confidence,
                TimeStamp = timestamp,
                HalfLifeSeconds = 0f
            };
        }

        if (observation.CharacterData.Health.HasValue)
        {
            Health = new Belief<float>
            {
                Value = observation.CharacterData.Health.Value,
                Confidence = confidence,
                TimeStamp = timestamp,
                HalfLifeSeconds = 0f
            };
        }

        if (observation.CharacterData.Equipped)
        {
            Equipped = new Belief<GameObject>
            {
                Value = observation.CharacterData.Equipped,
                Confidence = confidence,
                TimeStamp = timestamp,
                HalfLifeSeconds = 0f
            };
        }

        if (!string.IsNullOrWhiteSpace(observation.CharacterData.FactionId))
        {
            FactionId = new Belief<string>
            {
                Value = observation.CharacterData.FactionId,
                Confidence = confidence,
                TimeStamp = timestamp,
                HalfLifeSeconds = 0f
            };
        }
    }

    public void ApplyDecay(float currentTime, float retentionSeconds, float defaultHalfLifeSeconds)
    {
        Position = ApplyDecayToBelief(Position, currentTime, retentionSeconds, defaultHalfLifeSeconds);
        FacingDirection = ApplyDecayToBelief(FacingDirection, currentTime, retentionSeconds, defaultHalfLifeSeconds);
        Health = ApplyDecayToBelief(Health, currentTime, retentionSeconds, defaultHalfLifeSeconds);
        Equipped = ApplyDecayToBelief(Equipped, currentTime, retentionSeconds, defaultHalfLifeSeconds);
        FactionId = ApplyDecayToBelief(FactionId, currentTime, retentionSeconds, defaultHalfLifeSeconds);
        Stance = ApplyDecayToBelief(Stance, currentTime, retentionSeconds, defaultHalfLifeSeconds);
    }

    private static Belief<T>? ApplyDecayToBelief<T>(Belief<T>? belief, float currentTime, float retentionSeconds,
        float defaultHalfLifeSeconds)
    {
        if (!belief.HasValue)
            return belief;

        var data = belief.Value;
        var elapsed = Mathf.Max(0f, (float)(currentTime - data.TimeStamp));
        var halfLife = data.HalfLifeSeconds > 0f ? data.HalfLifeSeconds : defaultHalfLifeSeconds;

        if (halfLife > 0f)
        {
            var decayFactor = Mathf.Pow(0.5f, elapsed / halfLife);
            data.Confidence = Mathf.Clamp01(data.Confidence * decayFactor);
        }

        var shouldForget = (retentionSeconds > 0f && elapsed >= retentionSeconds) || data.Confidence <= 0.01f;
        return shouldForget ? null : data;
    }
}
