using UnityEngine;

/// <summary>
/// Base class for AI behaviours that respond to a specific intent type.
/// </summary>
public abstract class BehaviourBase : MonoBehaviour
{
    [Header("Knowledge")]
    [SerializeField] protected AgentKnowledge agentKnowledge;

    /// <summary>
    /// The intent type this behaviour knows how to handle.
    /// </summary>
    public abstract IntentType IntentType { get; }

    /// <summary>
    /// Knowledge component associated with the agent.
    /// </summary>
    public AgentKnowledge Knowledge => agentKnowledge;

    protected virtual void Awake()
    {
        if (!agentKnowledge)
            agentKnowledge = GetComponentInParent<AgentKnowledge>();
    }

    /// <summary>
    /// Called when the behaviour becomes active for a new intent.
    /// </summary>
    public virtual void BeginBehaviour(IIntent intent) { }

    /// <summary>
    /// Called every frame while the behaviour remains active for the current intent.
    /// </summary>
    public virtual void TickBehaviour(IIntent intent) { }

    /// <summary>
    /// Called when the behaviour is deactivated or superseded by another intent.
    /// </summary>
    public virtual void EndBehaviour() { }
}
