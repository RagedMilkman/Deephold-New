using UnityEngine;

/// <summary>
/// Suggests fleeing when other characters are nearby.
/// </summary>
public sealed class FleeConsideration : Consideration
{
    [SerializeField, Range(0f, 1f)] private float baseUrgency = 0.5f;
    [SerializeField] private float escapeDistance = 5f;

    public override IIntent EvaluateIntent(AgentKnowledge knowledge)
    {
        if (knowledge == null || knowledge.Characters.Count == 0)
            return null;

        var escapeDirection = Vector3.zero;

        foreach (var character in knowledge.Characters.Values)
        {
            if (character?.CharacterObject == null)
                continue;

            var directionAway = transform.position - character.CharacterObject.transform.position;
            escapeDirection += directionAway;
        }

        if (escapeDirection == Vector3.zero)
            return null;

        var normalizedDirection = escapeDirection.normalized;
        var escapeTarget = transform.position + normalizedDirection * escapeDistance;

        return new FleeIntent
        {
            Urgency = baseUrgency,
            EscapePos = escapeTarget
        };
    }
}
