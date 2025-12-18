using UnityEngine;

/// <summary>
/// Suggests fleeing when other characters are nearby.
/// </summary>
public sealed class FleeConsideration : Consideration
{
    [SerializeField, Range(0f, 1f)] private float baseUrgency = 0.5f;
    [SerializeField] private float escapeDistance = 5f;
    [SerializeField, Range(0f, 1f)] private float facingThreatWeight = 0.35f;
    [SerializeField, Range(0f, 1f)] private float equippedThreatBonus = 0.2f;
    [SerializeField, Range(0f, 1f)] private float activeStanceThreatBonus = 0.25f;
    [SerializeField, Range(0f, 2f)] private float lowHealthUrgencyMultiplier = 1f;

    public override IIntent EvaluateIntent(AgentKnowledge knowledge)
    {
        if (knowledge == null || knowledge.Characters.Count == 0 || escapeDistance <= 0f)
            return null;

        var escapeDirection = Vector3.zero;
        var highestThreat = 0f;
        var selfPosition = transform.position;
        var selfFactionId = knowledge.Self?.FactionId?.Value;
        var selfHealth = knowledge.Self?.Health?.Value;
        var selfHealthComponent = knowledge.Self?.CharacterObject != null
            ? knowledge.Self.CharacterObject.GetComponent<CharacterHealth>()
            : null;
        var maxHealth = selfHealthComponent != null ? selfHealthComponent.MaxHealth : (selfHealth ?? 0f);
        var lowHealthFactor = selfHealth.HasValue && maxHealth > 0f
            ? 1f - Mathf.Clamp01(selfHealth.Value / maxHealth)
            : 0f;

        foreach (var character in knowledge.Characters.Values)
        {
            if (character?.CharacterObject == null)
                continue;

            if (knowledge.Self != null && character.Id == knowledge.Self.Id)
                continue;

            var targetTransform = character.CharacterObject.transform;
            var directionAway = selfPosition - targetTransform.position;
            directionAway.y = 0f;

            if (directionAway.sqrMagnitude <= Mathf.Epsilon)
                continue;

            var otherFactionId = character.FactionId?.Value;
            var sameFaction = !string.IsNullOrWhiteSpace(selfFactionId)
                              && !string.IsNullOrWhiteSpace(otherFactionId)
                              && selfFactionId == otherFactionId;

            if (sameFaction)
                continue;

            var distance = directionAway.magnitude;
            var normalizedAway = directionAway / distance;
            var distanceFactor = Mathf.Clamp01(1f - (distance / escapeDistance));

            var facingFactor = 0f;
            if (character.FacingDirection.HasValue)
            {
                var facing = character.FacingDirection.Value;
                facing.Value.y = 0f;

                if (facing.Value.sqrMagnitude > 0.0001f)
                    facingFactor = Mathf.Max(0f, Vector3.Dot(facing.Value.normalized, normalizedAway));
            }

            var hasEquipped = character.Equipped.HasValue && character.Equipped.Value.Value != null;
            var inActiveStance = character.Stance?.Value == TopDownMotor.Stance.Active;

            var threat = distanceFactor;
            threat += facingFactor * facingThreatWeight;
            if (hasEquipped)
                threat += equippedThreatBonus;
            if (inActiveStance)
                threat += activeStanceThreatBonus;

            threat = Mathf.Clamp01(threat);

            escapeDirection += normalizedAway * threat;
            highestThreat = Mathf.Max(highestThreat, threat);
        }

        if (escapeDirection == Vector3.zero)
            return null;

        var normalizedDirection = escapeDirection.normalized;
        var escapeTarget = selfPosition + normalizedDirection * escapeDistance;
        var threatUrgency = Mathf.Lerp(baseUrgency, 1f, highestThreat);
        var urgency = Mathf.Clamp01(threatUrgency * (1f + lowHealthFactor * lowHealthUrgencyMultiplier));

        return new FleeIntent
        {
            Urgency = urgency,
            EscapePos = escapeTarget
        };
    }
}
