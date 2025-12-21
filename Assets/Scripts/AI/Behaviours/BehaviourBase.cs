using UnityEngine;

/// <summary>
/// Base class for AI behaviours that respond to a specific intent type.
/// </summary>
public abstract class BehaviourBase : MonoBehaviour
{
    /// <summary>
    /// The intent type this behaviour knows how to handle.
    /// </summary>
    public abstract IntentType IntentType { get; }

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
