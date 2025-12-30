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
    [SerializeField, Range(0f, 1f)] private float rangeTolerance = 0.1f;
    [SerializeField, Range(0f, 2f)] private float cautiousRangeMultiplier = 1.1f;
    [SerializeField] private bool sprintToTarget = true;
    [SerializeField] private bool faceTargetWhileMoving = true;

    private Vector2[] path = Array.Empty<Vector2>();
    private int pathIndex;
    private string currentTargetId;
    private Vector3 lastKnownTargetPosition;
    private float lastDesiredRange;

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

        float desiredRange = Mathf.Max(engageIntent.DesiredRange, waypointTolerance * 2f);
        if (engageIntent.UseCover)
            desiredRange *= cautiousRangeMultiplier;

        float toleranceDistance = desiredRange * rangeTolerance;
        float minDesiredRange = Mathf.Max(desiredRange - toleranceDistance, waypointTolerance * 0.5f);
        float maxDesiredRange = desiredRange + toleranceDistance;

        bool intentChanged = IntentChanged(engageIntent, targetPosition, desiredRange);
        if (intentChanged)
        {
            RebuildPath(engageIntent, targetPosition, desiredRange);
            pathIndex = 0;
        }

        var currentPosition = CurrentPosition;
        Vector3 toTarget = targetPosition - currentPosition;
        toTarget.y = 0f;
        float distanceSqr = toTarget.sqrMagnitude;
        bool withinActiveStanceRange = distanceSqr <= desiredRange * desiredRange;
        bool tooClose = distanceSqr < minDesiredRange * minDesiredRange;
        bool tooFar = distanceSqr > maxDesiredRange * maxDesiredRange;
        bool withinDesiredBand = !tooClose && !tooFar;

        if (combatActions)
            combatActions.SetActiveStance(withinActiveStanceRange);

        if (withinDesiredBand)
        {
            motorActions.MoveToPosition(currentPosition, targetPosition, true, false, Mathf.Max(minDesiredRange, waypointTolerance * 0.5f));
            path = Array.Empty<Vector2>();
            ClearDebugPath();
            if (targetTransform)
                motorActions.RotateToTarget(ResolveAimTransform(targetTransform));
            else
                motorActions.AimFromPosition(currentPosition, toTarget);
            return;
        }

        if (path == null || path.Length == 0)
            RebuildPath(engageIntent, targetPosition, desiredRange);

        if (path == null || path.Length == 0)
            return;

        pathIndex = motorActions.MoveToPathPosition(currentPosition, path, pathIndex, faceTargetWhileMoving, sprintToTarget, waypointTolerance);
        if (targetTransform)
            motorActions.RotateToTarget(ResolveAimTransform(targetTransform));
        else
            motorActions.AimFromPosition(currentPosition, toTarget);
    }

    public override void EndBehaviour()
    {
        StopPursuit();
        currentTargetId = null;
        lastKnownTargetPosition = Vector3.zero;
        lastDesiredRange = 0f;
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
        {
            RebuildPath(intent, targetPosition, intent.DesiredRange);
        }
        else
        {
            StopPursuit();
        }
    }

    private void RebuildPath(EngageIntent intent, Vector3 targetPosition, float desiredRange)
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

        var approachDistance = Mathf.Max(desiredRange, waypointTolerance * 2f);
        var desiredDestination = targetPosition - toTarget.normalized * approachDistance;
        desiredDestination.y = currentPosition.y;

        Vector3 destination = desiredDestination;
        if (TryResolveDestination(desiredDestination, out var resolved))
            destination = resolved;

        path = BuildPath(destination);
        UpdateDebugPath(path);

        currentTargetId = intent.TargetId;
        lastKnownTargetPosition = targetPosition;
        lastDesiredRange = desiredRange;
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

    private bool IntentChanged(EngageIntent intent, Vector3 targetPosition, float desiredRange)
    {
        bool targetChanged = currentTargetId != intent.TargetId;
        bool positionChanged = Vector3.SqrMagnitude(targetPosition - lastKnownTargetPosition) > repathDistance * repathDistance;
        bool rangeChanged = Math.Abs(desiredRange - lastDesiredRange) > 0.01f;

        return targetChanged || positionChanged || rangeChanged;
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
