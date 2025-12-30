using System;
using UnityEngine;

/// <summary>
/// Behaviour that steers the agent toward a target and holds an engagement distance.
/// </summary>
public class EngageBehaviour : PathingBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private AgentKnowledge knowledge;
    [SerializeField] private CombatActions combatActions;
    [SerializeField] private EngageTacticBehaviour[] tactics;

    [Header("Engagement")]
    [SerializeField, Min(0f)] private float repathDistance = 0.75f;
    [SerializeField, Range(0f, 2f)] private float cautiousRangeMultiplier = 1.1f;
    [SerializeField] private bool sprintToTarget = true;
    [SerializeField] private bool faceTargetWhileMoving = true;

    private readonly System.Collections.Generic.Dictionary<EngageTactic, EngageTacticBehaviour> tacticLookup
        = new System.Collections.Generic.Dictionary<EngageTactic, EngageTacticBehaviour>();

    private Vector2[] path = Array.Empty<Vector2>();
    private int pathIndex;
    private string currentTargetId;
    private Vector3 lastKnownTargetPosition;
    private float lastPreferredRange;

    protected override void Awake()
    {
        base.Awake();
        if (!knowledge)
            knowledge = GetComponentInParent<AgentKnowledge>();

        if (!combatActions)
            combatActions = GetComponentInParent<CombatActions>();

        if (tactics == null || tactics.Length == 0)
            tactics = GetComponents<EngageTacticBehaviour>();

        if (tactics != null)
        {
            foreach (var tactic in tactics)
            {
                if (tactic == null)
                    continue;

                if (tacticLookup.ContainsKey(tactic.TacticType))
                    continue;

                tactic.Initialize(this);
                tacticLookup.Add(tactic.TacticType, tactic);
            }
        }
    }

    public override IntentType IntentType => IntentType.Engage;

    public override void BeginBehaviour(IIntent intent)
    {
        pathIndex = 0;
        RebuildPath(intent as EngageIntent);
    }

    public override void TickBehaviour(IIntent intent)
    {
        if (motorActions == null)
            return;

        var engageIntent = intent as EngageIntent;
        if (engageIntent == null)
            return;

        if (!TryResolveTargetPosition(engageIntent, out var targetPosition, out var targetTransform))
        {
            StopPursuit();
            return;
        }

        var tacticsData = engageIntent.Tactics ?? new EngageTactics { Tactic = EngageTactic.Pursue };

        if (!tacticLookup.TryGetValue(tacticsData.Tactic, out var tactic))
        {
            if (tacticLookup.TryGetValue(EngageTactic.Pursue, out var defaultTactic))
                tactic = defaultTactic;
        }

        tactic?.Tick(engageIntent, tacticsData, targetPosition, targetTransform);
    }

    public override void EndBehaviour()
    {
        StopPursuit();
        currentTargetId = null;
        lastKnownTargetPosition = Vector3.zero;
        lastPreferredRange = 0f;

        foreach (var tactic in tacticLookup.Values)
            tactic?.OnEnd();
    }

    internal void StopPursuit()
    {
        path = Array.Empty<Vector2>();
        pathIndex = 0;
        ClearDebugPath();

        if (combatActions)
            combatActions.SetActiveStance(false);
    }

    internal void RebuildPath(EngageIntent intent)
    {
        if (intent == null)
        {
            StopPursuit();
            return;
        }

        if (TryResolveTargetPosition(intent, out var targetPosition, out _))
            RebuildPath(intent, targetPosition, ResolvePreferredRange(intent));
        else
        {
            StopPursuit();
        }
    }

    internal void RebuildPath(EngageIntent intent, Vector3 targetPosition, float preferredRange)
    {
        path = Array.Empty<Vector2>();
        ClearDebugPath();

        if (intent == null)
            return;

        var currentPosition = CurrentPosition;
        Vector3 toTarget = targetPosition - currentPosition;
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude <= 0.0001f)
            return;

        var approachDistance = Mathf.Max(preferredRange, waypointTolerance * 2f);
        var desiredDestination = targetPosition - toTarget.normalized * approachDistance;
        desiredDestination.y = currentPosition.y;

        Vector3 destination = desiredDestination;
        if (TryResolveDestination(desiredDestination, out var resolved))
            destination = resolved;

        path = BuildPath(destination);
        UpdateDebugPath(path);

        currentTargetId = intent.TargetId;
        lastKnownTargetPosition = targetPosition;
        lastPreferredRange = preferredRange;
    }

    internal bool TryResolveTargetPosition(EngageIntent intent, out Vector3 targetPosition, out Transform targetTransform)
    {
        targetPosition = intent != null ? intent.TargetPosition : Vector3.zero;
        targetTransform = null;
        bool hasPosition = targetPosition != Vector3.zero;

        if (intent != null && !string.IsNullOrWhiteSpace(intent.TargetId) && knowledge
            && knowledge.Characters.TryGetValue(intent.TargetId, out var character))
        {
            if (character.CharacterObject)
            {
                targetTransform = character.CharacterObject.transform;
                if (!hasPosition)
                {
                    targetPosition = targetTransform.position;
                    hasPosition = true;
                }
            }

            if (character.Position.HasValue)
            {
                targetPosition = character.Position.Value.Value;
                hasPosition = true;
            }
        }

        return hasPosition;
    }

    internal bool IntentChanged(EngageIntent intent, Vector3 targetPosition, float preferredRange)
    {
        bool targetChanged = currentTargetId != intent.TargetId;
        bool positionChanged = Vector3.SqrMagnitude(targetPosition - lastKnownTargetPosition) > repathDistance * repathDistance;
        bool rangeChanged = Math.Abs(preferredRange - lastPreferredRange) > 0.01f;

        return targetChanged || positionChanged || rangeChanged;
    }

    private float ResolvePreferredRange(EngageIntent intent)
    {
        var pursue = intent?.Tactics?.Pursue;
        float minRange = pursue?.MinDesiredRange ?? 0f;
        float preferredRange = pursue?.PreferredDistance ?? 0f;
        float maxRange = pursue?.MaxDesiredRange ?? 0f;
        bool useCover = pursue?.UseCover ?? false;
        float cautiousMultiplier = cautiousRangeMultiplier;

        if (pursue == null && tacticLookup.TryGetValue(EngageTactic.Pursue, out var tactic)
            && tactic is PursueEngageTactic pursueTactic)
        {
            minRange = pursueTactic.DefaultMinRange;
            preferredRange = pursueTactic.DefaultPreferredDistance;
            maxRange = pursueTactic.DefaultMaxRange;
            useCover = pursueTactic.DefaultUseCover;
            cautiousMultiplier = pursueTactic.DefaultCautiousRangeMultiplier;
        }

        minRange = Mathf.Max(minRange, waypointTolerance * 0.5f);
        maxRange = Mathf.Max(minRange, maxRange);
        preferredRange = Mathf.Clamp(preferredRange, minRange, maxRange);

        if (useCover)
            preferredRange *= cautiousMultiplier;

        return preferredRange;
    }

    internal Vector2[] CurrentPath
    {
        get => path;
        set => path = value ?? Array.Empty<Vector2>();
    }

    internal int CurrentPathIndex
    {
        get => pathIndex;
        set => pathIndex = value;
    }

    internal float CautiousRangeMultiplier => cautiousRangeMultiplier;
    internal float RepathDistance => repathDistance;
    internal bool FaceTargetWhileMoving => faceTargetWhileMoving;
    internal bool SprintToTarget => sprintToTarget;
    internal CombatActions CombatActions => combatActions;
    internal MotorActions MotorActions => motorActions;
    internal float WaypointTolerance => waypointTolerance;
    internal Vector3 CurrentPosition => base.CurrentPosition;

    internal void ClearPath()
    {
        path = Array.Empty<Vector2>();
        pathIndex = 0;
        ClearDebugPath();
    }

}
