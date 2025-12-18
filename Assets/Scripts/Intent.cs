public enum IntentType
{
    Engage,
    Flee,
    Explore
}

public interface IIntent
{
    IntentType Type { get; }
    float Urgency { get; }
}
