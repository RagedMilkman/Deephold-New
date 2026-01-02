using System.Collections.Generic;
using UnityEngine;

public class CommunicationSense : MonoBehaviour, ISense
{
    [SerializeField, Min(0f)] private float communicationRange = 30f;
    [SerializeField, Min(0f)] private float deliveryDelaySeconds = 0.5f;
    [SerializeField, Min(0.01f)] private float broadcastIntervalSeconds = 1f;
    [SerializeField] private CharacterData selfCharacter;

    private readonly List<PendingTransmission> pendingTransmissions = new List<PendingTransmission>();
    private float lastBroadcastTime = -Mathf.Infinity;

    private void Awake()
    {
        if (!selfCharacter)
            selfCharacter = GetComponentInParent<CharacterData>();
    }

    public List<Observation> GetObservations()
    {
        var now = Time.time;
        var readyObservations = CollectReadyTransmissions(now);

        if (now - lastBroadcastTime >= broadcastIntervalSeconds)
        {
            QueueIncomingTransmissions(now);
            lastBroadcastTime = now;
        }

        return readyObservations;
    }

    private List<Observation> CollectReadyTransmissions(float currentTime)
    {
        var readyObservations = new List<Observation>();
        for (int i = pendingTransmissions.Count - 1; i >= 0; i--)
        {
            var transmission = pendingTransmissions[i];
            if (currentTime < transmission.DeliveryTime)
                continue;

            if (transmission.Observations != null && transmission.Observations.Count > 0)
                readyObservations.AddRange(transmission.Observations);

            pendingTransmissions.RemoveAt(i);
        }

        return readyObservations;
    }

    private void QueueIncomingTransmissions(float currentTime)
    {
        if (!selfCharacter || !selfCharacter.Faction)
            return;

        var factionMembers = selfCharacter.Faction.Members;
        foreach (var member in factionMembers)
        {
            if (!member || member == selfCharacter)
                continue;

            if (Vector3.Distance(transform.position, member.transform.position) > communicationRange)
                continue;

            var knowledge = member.GetComponentInChildren<AgentKnowledge>();
            if (!knowledge)
                continue;

            var observations = BuildObservationsFromKnowledge(knowledge);
            if (observations.Count == 0)
                continue;

            pendingTransmissions.Add(new PendingTransmission(currentTime + deliveryDelaySeconds, observations));
        }
    }

    private List<Observation> BuildObservationsFromKnowledge(AgentKnowledge sourceKnowledge)
    {
        var observations = new List<Observation>();

        if (sourceKnowledge.Self != null)
            observations.AddRange(CreateObservationsForCharacter(sourceKnowledge.Self));

        foreach (var kvp in sourceKnowledge.Characters)
        {
            if (kvp.Value != null)
                observations.AddRange(CreateObservationsForCharacter(kvp.Value));
        }

        return observations;
    }

    private List<Observation> CreateObservationsForCharacter(CharacterKnowledge characterKnowledge)
    {
        var observations = new List<Observation>();
        var characterObject = characterKnowledge.CharacterObject;
        var id = characterKnowledge.Id;

        Transform positionRoot = null;
        float? health = null;
        GameObject equipped = null;
        string factionId = null;
        Vector3? facingDirection = null;
        TopDownMotor.Stance? stance = null;

        if (characterKnowledge.Position.HasValue)
            positionRoot = characterKnowledge.Position.Value.Value.Transform;
        if (characterKnowledge.Health.HasValue)
            health = characterKnowledge.Health.Value.Value;
        if (characterKnowledge.Equipped.HasValue)
            equipped = characterKnowledge.Equipped.Value.Value;
        if (characterKnowledge.FactionId.HasValue)
            factionId = characterKnowledge.FactionId.Value.Value;
        if (characterKnowledge.FacingDirection.HasValue)
            facingDirection = characterKnowledge.FacingDirection.Value.Value;
        if (characterKnowledge.Stance.HasValue)
            stance = characterKnowledge.Stance.Value.Value;

        if (characterKnowledge.Position.HasValue)
        {
            var belief = characterKnowledge.Position.Value;
            observations.Add(Observation.ForCharacter(positionRoot, characterObject, id, null, null, null, null, null,
                BeliefSource.Communication, Mathf.Clamp01(belief.Confidence), (float)belief.TimeStamp));
        }

        if (characterKnowledge.Health.HasValue)
        {
            var belief = characterKnowledge.Health.Value;
            observations.Add(Observation.ForCharacter(positionRoot, characterObject, id, belief.Value, null, null, null, null,
                BeliefSource.Communication, Mathf.Clamp01(belief.Confidence), (float)belief.TimeStamp));
        }

        if (characterKnowledge.Equipped.HasValue)
        {
            var belief = characterKnowledge.Equipped.Value;
            observations.Add(Observation.ForCharacter(positionRoot, characterObject, id, null, belief.Value, null, null, null,
                BeliefSource.Communication, Mathf.Clamp01(belief.Confidence), (float)belief.TimeStamp));
        }

        if (characterKnowledge.FactionId.HasValue)
        {
            var belief = characterKnowledge.FactionId.Value;
            observations.Add(Observation.ForCharacter(positionRoot, characterObject, id, null, null, belief.Value, null, null,
                BeliefSource.Communication, Mathf.Clamp01(belief.Confidence), (float)belief.TimeStamp));
        }

        if (characterKnowledge.FacingDirection.HasValue)
        {
            var belief = characterKnowledge.FacingDirection.Value;
            observations.Add(Observation.ForCharacter(positionRoot, characterObject, id, null, null, null, belief.Value, null,
                BeliefSource.Communication, Mathf.Clamp01(belief.Confidence), (float)belief.TimeStamp));
        }

        if (characterKnowledge.Stance.HasValue)
        {
            var belief = characterKnowledge.Stance.Value;
            observations.Add(Observation.ForCharacter(positionRoot, characterObject, id, null, null, null, null, belief.Value,
                BeliefSource.Communication, Mathf.Clamp01(belief.Confidence), (float)belief.TimeStamp));
        }

        // If we have at least one belief, also share a combined snapshot to help receivers with missing context.
        if (observations.Count > 0)
        {
            float snapshotConfidence = 0f;
            float snapshotTime = 0f;
            if (characterKnowledge.Position.HasValue)
            {
                snapshotConfidence = Mathf.Max(snapshotConfidence, characterKnowledge.Position.Value.Confidence);
                snapshotTime = Mathf.Max(snapshotTime, (float)characterKnowledge.Position.Value.TimeStamp);
            }
            if (characterKnowledge.Health.HasValue)
            {
                snapshotConfidence = Mathf.Max(snapshotConfidence, characterKnowledge.Health.Value.Confidence);
                snapshotTime = Mathf.Max(snapshotTime, (float)characterKnowledge.Health.Value.TimeStamp);
            }
            if (characterKnowledge.Equipped.HasValue)
            {
                snapshotConfidence = Mathf.Max(snapshotConfidence, characterKnowledge.Equipped.Value.Confidence);
                snapshotTime = Mathf.Max(snapshotTime, (float)characterKnowledge.Equipped.Value.TimeStamp);
            }
            if (characterKnowledge.FactionId.HasValue)
            {
                snapshotConfidence = Mathf.Max(snapshotConfidence, characterKnowledge.FactionId.Value.Confidence);
                snapshotTime = Mathf.Max(snapshotTime, (float)characterKnowledge.FactionId.Value.TimeStamp);
            }
            if (characterKnowledge.FacingDirection.HasValue)
            {
                snapshotConfidence = Mathf.Max(snapshotConfidence, characterKnowledge.FacingDirection.Value.Confidence);
                snapshotTime = Mathf.Max(snapshotTime, (float)characterKnowledge.FacingDirection.Value.TimeStamp);
            }
            if (characterKnowledge.Stance.HasValue)
            {
                snapshotConfidence = Mathf.Max(snapshotConfidence, characterKnowledge.Stance.Value.Confidence);
                snapshotTime = Mathf.Max(snapshotTime, (float)characterKnowledge.Stance.Value.TimeStamp);
            }

            observations.Add(Observation.ForCharacter(positionRoot, characterObject, id, health, equipped, factionId, facingDirection,
                stance, BeliefSource.Communication, Mathf.Clamp01(snapshotConfidence), snapshotTime));
        }

        return observations;
    }

    private struct PendingTransmission
    {
        public float DeliveryTime { get; private set; }
        public List<Observation> Observations { get; private set; }

        public PendingTransmission(float deliveryTime, List<Observation> observations)
        {
            DeliveryTime = deliveryTime;
            Observations = observations;
        }
    }
}
