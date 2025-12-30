using UnityEngine;

public abstract class EngageTacticBehaviour : PathingBehaviour
{
    protected EngageBehaviour behaviour;
    protected AgentKnowledge knowledge;
    protected CombatActions combatActions;

    public override IntentType IntentType => IntentType.Engage;
    public abstract EngageTactic TacticType { get; }

    internal virtual void Initialize(EngageBehaviour engageBehaviour, AgentKnowledge knowledgeSource, CombatActions combat)
    {
        behaviour = engageBehaviour;
        knowledge = knowledgeSource ? knowledgeSource : GetComponentInParent<AgentKnowledge>();
        combatActions = combat ? combat : GetComponentInParent<CombatActions>();
    }

    public virtual void OnBegin(EngageIntent intent) { }

    public abstract void Tick(EngageIntent intent);

    public virtual void OnEnd() { }
}
