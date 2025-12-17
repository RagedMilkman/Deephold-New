using UnityEngine;

public enum BeliefSource
{
    Sight,
    Hearing,
    Inferred,
    Communication
}

public struct Belief<T>
{
    public T Value;
    public float Confidence;     // 0..1
    public double TimeStamp;     // Time.timeAsDouble
    public float DecayPerSecond; // optional for now
}
