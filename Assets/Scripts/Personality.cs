using UnityEngine;

/// <summary>
/// Contains adjustable personality traits for an AI character.
/// </summary>
[System.Serializable]
public sealed class Personality
{
    [SerializeField, Range(0f, 1f)] private float aggression = 0.5f;
    [SerializeField, Range(0f, 1f)] private float bravery = 0.5f;

    public float Aggression => aggression;
    public float Bravery => bravery;
}
