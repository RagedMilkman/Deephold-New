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

    [Header("Engagement")]
    [SerializeField, Min(0f)] private float repathDistance = 0.75f;
    [SerializeField, Range(0f, 2f)] private float cautiousRangeMultiplier = 1.1f;
    [SerializeField] private bool sprintToTarget = true;
    [SerializeField] private bool faceTargetWhileMoving = true;

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

        var tactics = engageIntent.Tactics ?? new EngageTactics { Tactic = EngageTactic.Pursue };

        switch (tactics.Tactic)
        {
            case EngageTactic.Pursue:
                TickPursue(engageIntent, tactics.Pursue, targetPosition, targetTransform);
                break;
            default:
                TickPursue(engageIntent, tactics.Pursue, targetPosition, targetTransform);
                break;
        }
    }

    public override void EndBehaviour()
    {
        StopPursuit();
        currentTargetId = null;
        lastKnownTargetPosition = Vector3.zero;
        lastPreferredRange = 0f;
    }

    private void StopPursuit()
    {
        path = Array.Empty<Vector2>();
        pathIndex = 0;
        ClearDebugPath();

        if (combatActions)
            combatActions.SetActiveStance(false);
    }

    private void RebuildPath(EngageIntent intent)
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

    private void RebuildPath(EngageIntent intent, Vector3 targetPosition, float preferredRange)
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

    private bool TryResolveTargetPosition(EngageIntent intent, out Vector3 targetPosition, out Transform targetTransform)
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

    private bool IntentChanged(EngageIntent intent, Vector3 targetPosition, float preferredRange)
    {
        bool targetChanged = currentTargetId != intent.TargetId;
        bool positionChanged = Vector3.SqrMagnitude(targetPosition - lastKnownTargetPosition) > repathDistance * repathDistance;
        bool rangeChanged = Math.Abs(preferredRange - lastPreferredRange) > 0.01f;

        return targetChanged || positionChanged || rangeChanged;
    }

    private void TickPursue(EngageIntent engageIntent, PursueTactic pursue, Vector3 targetPosition, Transform targetTransform)
    {
        pursue ??= new PursueTactic();

        var currentPosition = CurrentPosition;
        Vector3 toTarget = targetPosition - currentPosition;
        toTarget.y = 0f;

        float minRange = Mathf.Max(pursue.MinDesiredRange, waypointTolerance * 0.5f);
        float maxRange = Mathf.Max(minRange, pursue.MaxDesiredRange);
        float preferredRange = Mathf.Clamp(pursue.PreferredDistance, minRange, maxRange);

        if (pursue.UseCover)
        {
            minRange *= cautiousRangeMultiplier;
            maxRange *= cautiousRangeMultiplier;
            preferredRange *= cautiousRangeMultiplier;
        }

        bool intentChanged = IntentChanged(engageIntent, targetPosition, preferredRange);
        if (intentChanged)
        {
            RebuildPath(engageIntent, targetPosition, preferredRange);
            pathIndex = 0;
        }

        float distanceSqr = toTarget.sqrMagnitude;
        bool withinActiveStanceRange = distanceSqr <= maxRange * maxRange;
        bool withinDesiredRange = distanceSqr >= minRange * minRange && distanceSqr <= maxRange * maxRange;

        if (combatActions)
            combatActions.SetActiveStance(withinActiveStanceRange);

        if (withinDesiredRange)
        {
            motorActions.MoveToPosition(currentPosition, targetPosition, true, false, Mathf.Max(minRange, waypointTolerance * 0.5f));
            path = Array.Empty<Vector2>();
            ClearDebugPath();
            if (targetTransform)
                motorActions.RotateToTarget(ResolveAimTransform(targetTransform));
            else
                motorActions.AimFromPosition(currentPosition, toTarget);
            return;
        }

        if (path == null || path.Length == 0)
            RebuildPath(engageIntent, targetPosition, preferredRange);

        if (path == null || path.Length == 0)
            return;

        pathIndex = motorActions.MoveToPathPosition(currentPosition, path, pathIndex, faceTargetWhileMoving, sprintToTarget, waypointTolerance);
        if (targetTransform)
            motorActions.RotateToTarget(ResolveAimTransform(targetTransform));
        else
            motorActions.AimFromPosition(currentPosition, toTarget);
    }

    private float ResolvePreferredRange(EngageIntent intent)
    {
        var pursue = intent?.Tactics?.Pursue;
        if (pursue == null)
            return waypointTolerance * 2f;

        float minRange = Mathf.Max(pursue.MinDesiredRange, waypointTolerance * 0.5f);
        float maxRange = Mathf.Max(minRange, pursue.MaxDesiredRange);
        float preferredRange = Mathf.Clamp(pursue.PreferredDistance, minRange, maxRange);

        if (pursue.UseCover)
            preferredRange *= cautiousRangeMultiplier;

        return preferredRange;
    }

    private static Transform ResolveAimTransform(Transform target)
    {
        if (!target)
            return target;

        var health = target.GetComponentInParent<CharacterHealth>();
        if (health && health.Animator)
        {
            var chest = health.Animator.GetBoneTransform(HumanBodyBones.Chest)
                        ?? health.Animator.GetBoneTransform(HumanBodyBones.UpperChest)
                        ?? health.Animator.GetBoneTransform(HumanBodyBones.Spine);

            if (chest)
                return chest;
        }

        return target;
    }
}
