using UnityEngine;

/// <summary>
/// Suggests engaging hostile targets when personality and proximity allow.
/// </summary>
public sealed class EngageConsideration : Consideration
{
    [Header("Engagement")]
    [SerializeField, Range(0f, 1f)] private float baseUrgency = 0.35f;
    [SerializeField, Min(0f)] private float maxEngageDistance = 12f;
    [SerializeField, Min(0f)] private float minDesiredRange = 1.5f;
    [SerializeField, Min(0f)] private float maxDesiredRange = 6f;

    [Header("Threat Evaluation")]
    [SerializeField, Range(0f, 1f)] private float proximityWeight = 0.55f;
    [SerializeField, Range(0f, 1f)] private float facingThreatWeight = 0.35f;
    [SerializeField, Range(0f, 1f)] private float equippedThreatBonus = 0.15f;
    [SerializeField, Range(0f, 1f)] private float activeStanceThreatBonus = 0.2f;

    [Header("Personality Weights")]
    [SerializeField, Range(0f, 2f)] private float aggressionInfluence = 1.2f;
    [SerializeField, Range(0f, 2f)] private float braveryInfluence = 1.1f;

    public override IIntent EvaluateIntent(AgentKnowledge knowledge, Personality personality)
    {
        if (knowledge == null || knowledge.Characters.Count == 0 || maxEngageDistance <= 0f)
            return null;

        var self = knowledge.Self;
        if (self == null || !self.Position.HasValue)
            return null;

        var selfPosition = self.Position.Value.Value;
        var selfFactionId = self.FactionId?.Value;
        var aggression = personality?.Aggression ?? 0.5f;
        var bravery = personality?.Bravery ?? 0.5f;

        CharacterKnowledge bestTarget = null;
        Vector3 bestTargetPosition = Vector3.zero;
        float bestScore = 0f;

        foreach (var character in knowledge.Characters.Values)
        {
            if (character == null || character.Id == self.Id)
                continue;

            if (character.Position == null || !character.Position.HasValue)
                continue;

            var position = character.Position.Value.Value;
            var distance = Vector3.Distance(selfPosition, position);
            if (distance <= Mathf.Epsilon || distance > maxEngageDistance)
                continue;

            var otherFactionId = character.FactionId?.Value;
            var sameFaction = !string.IsNullOrWhiteSpace(selfFactionId)
                              && !string.IsNullOrWhiteSpace(otherFactionId)
                              && selfFactionId == otherFactionId;
            if (sameFaction)
                continue;

            float proximity = Mathf.Clamp01(1f - distance / maxEngageDistance);
            float facingFactor = 0f;
            if (character.FacingDirection.HasValue)
            {
                var facing = character.FacingDirection.Value.Value;
                facing.y = 0f;

                var toSelf = selfPosition - position;
                toSelf.y = 0f;

                if (facing.sqrMagnitude > 0.0001f && toSelf.sqrMagnitude > 0.0001f)
                    facingFactor = Mathf.Max(0f, Vector3.Dot(facing.normalized, toSelf.normalized));
            }

            bool hasEquipped = character.Equipped.HasValue && character.Equipped.Value.Value != null;
            bool inActiveStance = character.Stance?.Value == TopDownMotor.Stance.Active;

            float threat = proximity * proximityWeight;
            threat += facingFactor * facingThreatWeight;
            if (hasEquipped)
                threat += equippedThreatBonus;
            if (inActiveStance)
                threat += activeStanceThreatBonus;

            threat = Mathf.Clamp01(threat);

            var aggressionWeight = Mathf.Lerp(0.5f, aggressionInfluence, aggression);
            var braveryWeight = Mathf.Lerp(0.5f, braveryInfluence, bravery);

            float score = proximity * aggressionWeight + threat * braveryWeight;
            if (score <= bestScore)
                continue;

            bestScore = score;
            bestTarget = character;
            bestTargetPosition = position;
        }

        if (bestTarget == null)
            return null;

        float desiredRange = Mathf.Lerp(maxDesiredRange, minDesiredRange, Mathf.Clamp01((aggression + bravery) * 0.5f));
        bool useCover = bravery < 0.45f;
        float urgency = Mathf.Clamp01(baseUrgency + bestScore * 0.75f);

        if (urgency <= 0f)
            return null;

        return new EngageIntent
        {
            Urgency = urgency,
            TargetId = bestTarget.Id,
            TargetPosition = bestTargetPosition,
            DesiredRange = desiredRange,
            UseCover = useCover
        };
    }
}
