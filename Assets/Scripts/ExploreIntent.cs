using UnityEngine;

/// <summary>
/// Intent representing a desire to explore the surrounding area.
/// </summary>
public sealed class ExploreIntent : IIntent
{
    public IntentType Type => IntentType.Explore;
    public float Urgency { get; set; }

    public Vector3 Destination;
}
