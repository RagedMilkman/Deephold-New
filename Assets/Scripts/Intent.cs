public enum IntentType
{
    Engage,
    Flee
}

public interface IIntent
{
    IntentType Type { get; }
    float Urgency { get; }
}
