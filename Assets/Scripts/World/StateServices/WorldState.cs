using UnityEngine;

/// <summary>
/// Base component representing a single world state.
/// Implementations expose a read-only <see cref="IsActive"/> flag
/// that other systems can consume through <see cref="WorldStateService"/>.
/// </summary>
public abstract class WorldState : MonoBehaviour
{
    /// <summary>
    /// Service that manages this state.
    /// </summary>
    protected WorldStateService Service { get; private set; }

    /// <summary>
    /// Whether the state is currently considered active.
    /// </summary>
    public abstract bool IsActive { get; }

    /// <summary>
    /// Time elapsed according to the managing service, or zero if unassigned.
    /// </summary>
    protected float ElapsedTime => Service != null ? Service.ElapsedTime : 0f;

    /// <summary>
    /// Called by <see cref="WorldStateService"/> when the state is registered.
    /// </summary>
    /// <param name="service">The managing service.</param>
    internal void SetService(WorldStateService service)
    {
        Service = service;
        OnServiceAssigned();
    }

    /// <summary>
    /// Called by <see cref="WorldStateService"/> when the service is destroyed.
    /// </summary>
    /// <param name="service">The managing service.</param>
    internal void ClearService(WorldStateService service)
    {
        if (Service == service)
            Service = null;
    }

    /// <summary>
    /// Optional hook for derived classes when the managing service is assigned.
    /// </summary>
    protected virtual void OnServiceAssigned()
    {
    }
}
