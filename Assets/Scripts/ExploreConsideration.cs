using UnityEngine;

/// <summary>
/// Suggests exploring the surrounding area when no higher-priority action exists.
/// </summary>
public sealed class ExploreConsideration : Consideration
{
    [SerializeField, Range(0f, 1f)] private float baseUrgency = 0.15f;
    [SerializeField, Min(0f)] private float minDistance = 2f;
    [SerializeField, Min(0f)] private float maxDistance = 12f;

    public override IIntent EvaluateIntent(AgentKnowledge knowledge)
    {
        if (knowledge == null || maxDistance <= 0f)
            return null;

        var direction = Random.insideUnitSphere;
        direction.y = 0f;

        if (direction.sqrMagnitude <= Mathf.Epsilon)
            return null;

        var lowerBound = Mathf.Min(minDistance, maxDistance);
        var upperBound = Mathf.Max(minDistance, maxDistance);
        var distance = Mathf.Clamp(Random.Range(lowerBound, upperBound), 0f, upperBound);
        var destination = transform.position + direction.normalized * distance;

        return new ExploreIntent
        {
            Urgency = baseUrgency,
            Destination = destination
        };
    }
}
