using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(ServiceExecutionOrder.WorldStateService)]
public class WorldStateService : MonoBehaviour
{
    [SerializeField]
    private List<WorldState> worldStates = new List<WorldState>();

    readonly Dictionary<Type, WorldState> _stateLookup = new Dictionary<Type, WorldState>();
    float _startTime;

    /// <summary>
    /// Time in seconds since the service started tracking states.
    /// </summary>
    public float ElapsedTime => Time.time - _startTime;

    /// <summary>
    /// Read-only access to the configured world states.
    /// </summary>
    public IReadOnlyList<WorldState> States => worldStates;

    void Awake()
    {
        _startTime = Time.time;
        InitialiseStates();
    }

    void OnDestroy()
    {
        foreach (var state in worldStates)
        {
            state?.ClearService(this);
        }
        _stateLookup.Clear();
    }

    void OnValidate()
    {
        if (worldStates == null)
            worldStates = new List<WorldState>();

        worldStates.RemoveAll(s => s == null);

        var attachedStates = GetComponents<WorldState>();
        for (int i = 0; i < attachedStates.Length; i++)
        {
            var state = attachedStates[i];
            if (state && !worldStates.Contains(state))
                worldStates.Add(state);
        }
    }

    void InitialiseStates()
    {
        _stateLookup.Clear();

        foreach (var state in worldStates)
        {
            if (!state)
                continue;

            var type = state.GetType();
            if (_stateLookup.ContainsKey(type))
            {
                Debug.LogWarning($"WorldStateService: duplicate state of type {type.Name} ignored.");
                continue;
            }

            state.SetService(this);
            _stateLookup.Add(type, state);
        }
    }

    /// <summary>
    /// Register a world state at runtime.
    /// </summary>
    public bool RegisterState(WorldState state)
    {
        if (!state)
            return false;

        var type = state.GetType();
        if (_stateLookup.ContainsKey(type))
        {
            Debug.LogWarning($"WorldStateService: duplicate state of type {type.Name} ignored.");
            return false;
        }

        worldStates.Add(state);
        state.SetService(this);
        _stateLookup.Add(type, state);
        return true;
    }

    /// <summary>
    /// Unregister an existing world state.
    /// </summary>
    public bool UnregisterState(WorldState state)
    {
        if (!state)
            return false;

        if (!_stateLookup.TryGetValue(state.GetType(), out var existing) || existing != state)
            return false;

        _stateLookup.Remove(state.GetType());
        worldStates.Remove(state);
        state.ClearService(this);
        return true;
    }

    /// <summary>
    /// Returns whether a specific state type is active.
    /// </summary>
    public bool IsStateActive<TState>() where TState : WorldState
    {
        return TryGetState<TState>(out var state) && state.IsActive;
    }

    /// <summary>
    /// Try to get a world state of the specified type.
    /// </summary>
    public bool TryGetState<TState>(out TState state) where TState : WorldState
    {
        if (TryGetState(typeof(TState), out var baseState))
        {
            state = baseState as TState;
            return state != null;
        }

        state = null;
        return false;
    }

    /// <summary>
    /// Try to get a world state by type at runtime.
    /// </summary>
    public bool TryGetState(Type stateType, out WorldState state)
    {
        if (stateType == null || !typeof(WorldState).IsAssignableFrom(stateType))
        {
            state = null;
            return false;
        }

        if (_stateLookup.TryGetValue(stateType, out var result))
        {
            state = result;
            return true;
        }

        foreach (var pair in _stateLookup)
        {
            if (stateType.IsAssignableFrom(pair.Key))
            {
                state = pair.Value;
                return true;
            }
        }

        state = null;
        return false;
    }
}
