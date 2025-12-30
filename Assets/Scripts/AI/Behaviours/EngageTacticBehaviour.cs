using UnityEngine;

public abstract class EngageTacticBehaviour : MonoBehaviour
{
    protected EngageBehaviour behaviour;

    public abstract EngageTactic TacticType { get; }

    internal virtual void Initialize(EngageBehaviour engageBehaviour)
    {
        behaviour = engageBehaviour;
    }

    public abstract void Tick(EngageIntent intent, EngageTactics tacticsData, Vector3 targetPosition, Transform targetTransform);

    public virtual void OnEnd()
    {
    }
}
