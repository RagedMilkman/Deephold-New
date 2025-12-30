using System;
using UnityEngine;

public sealed class PursueEngageTactic : EngageTacticBehaviour
{
    [Header("Desired Range")]
    [SerializeField, Min(0f)] private float minDesiredRange = 1.5f;
    [SerializeField, Min(0f)] private float preferredDistance = 3.5f;
    [SerializeField, Min(0f)] private float maxDesiredRange = 6f;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float repathDistance = 0.75f;
    [SerializeField] private bool sprintToTarget = true;
    [SerializeField] private bool faceTargetWhileMoving = true;

    private Vector2[] path = Array.Empty<Vector2>();
    private int pathIndex;
    private string currentTargetId;
    private Vector3 lastKnownTargetPosition;
    private float lastPreferredRange;

    public override EngageTactic TacticType => EngageTactic.Pursue;

    public override void OnBegin(EngageIntent intent)
    {
        pathIndex = 0;
        path = Array.Empty<Vector2>();
        currentTargetId = null;
        lastKnownTargetPosition = Vector3.zero;
        lastPreferredRange = 0f;
    }

    public override void Tick(EngageIntent intent)
    {
        if (intent == null)
            return;

        if (motorActions == null)
            return;

        if (!TryResolveTargetPosition(intent, out var targetPosition, out var targetTransform))
        {
            StopPursuit();
            return;
        }

        var pursueIntent = intent.Tactics?.Pursue;
        float minRange = Mathf.Max(ResolveValue(pursueIntent?.MinDesiredRange, minDesiredRange), waypointTolerance * 0.5f);
        float maxRange = Mathf.Max(minRange, ResolveValue(pursueIntent?.MaxDesiredRange, maxDesiredRange));
        float preferredRange = Mathf.Clamp(ResolveValue(pursueIntent?.PreferredDistance, preferredDistance), minRange, maxRange);

        float minRangeSqr = minRange * minRange;
        float maxRangeSqr = maxRange * maxRange;

        Vector3 currentPosition = CurrentPosition;
        Vector3 toTarget = targetPosition - currentPosition;
        toTarget.y = 0f;

        float distanceSqr = toTarget.sqrMagnitude;
        bool tooClose = distanceSqr < minRangeSqr;
        bool tooFar = distanceSqr > maxRangeSqr;
        bool withinDesiredRange = !tooClose && !tooFar;

        // Within max range: Enter active stance
        bool withinActiveStanceRange = distanceSqr <= maxRangeSqr;
        if (combatActions)
            combatActions.SetActiveStance(withinActiveStanceRange);

        if (IntentChanged(intent, targetPosition, preferredRange))
        {
            RebuildPath(intent, targetPosition, preferredRange);
            pathIndex = 0;
        }

        if (withinDesiredRange)
        {
            motorActions.MoveToPosition(currentPosition, targetPosition, true, false, Mathf.Max(minRange, waypointTolerance * 0.5f));
            ClearPath();
            // Look at target
            AimAtTarget(targetTransform, currentPosition, toTarget);
            return;
        }

        if (path == null || path.Length == 0)
            RebuildPath(intent, targetPosition, preferredRange);

        if (path == null || path.Length == 0)
            return;

        // Too close: move to preferred distance
        // Too far: move to preferred distance
        pathIndex = motorActions.MoveToPathPosition(currentPosition, path, pathIndex, faceTargetWhileMoving, sprintToTarget, waypointTolerance);
        // Look at target
        AimAtTarget(targetTransform, currentPosition, toTarget);
    }

    public override void OnEnd()
    {
        StopPursuit();
    }

    private void StopPursuit()
    {
        ClearPath();
        currentTargetId = null;
        if (combatActions)
            combatActions.SetActiveStance(false);
    }

    private void RebuildPath(EngageIntent intent, Vector3 targetPosition, float preferredRange)
    {
        path = Array.Empty<Vector2>();
        ClearDebugPath();

        Vector3 currentPosition = CurrentPosition;
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
        targetPosition = intent.TargetPosition;
        targetTransform = null;
        bool hasPosition = targetPosition != Vector3.zero;

        if (!string.IsNullOrWhiteSpace(intent.TargetId) && knowledge
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

    private void ClearPath()
    {
        path = Array.Empty<Vector2>();
        pathIndex = 0;
        ClearDebugPath();
    }

    private void AimAtTarget(Transform targetTransform, Vector3 currentPosition, Vector3 toTarget)
    {
        if (targetTransform)
            motorActions.RotateToTarget(ResolveAimTransform(targetTransform));
        else
            motorActions.AimFromPosition(currentPosition, toTarget);
    }

    private static float ResolveValue(float? intentValue, float defaultValue)
    {
        if (intentValue.HasValue && intentValue.Value > 0f)
            return intentValue.Value;

        return defaultValue;
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
