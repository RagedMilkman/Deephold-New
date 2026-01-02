using System;
using UnityEngine;

public sealed class PursueEngageTactic : EngageTacticBehaviour
{
    [Header("Fallback Weapon Range")]
    [SerializeField] private WeaponRange fallbackWeaponRange = new WeaponRange(0f, 1.5f, 3.5f, 6f, 6f);

    [Header("Movement")]
    [SerializeField, Min(0f)] private float repathDistance = 0.75f;
    [SerializeField] private bool sprintToTarget = true;
    [SerializeField] private bool faceTargetWhileMoving = true;

    [Header("Combat")]
    [SerializeField, Range(0f, 180f)] private float facingThresholdDegrees = 10f;

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

        if (IsTargetDead(intent))
        {
            StopPursuit();
            return;
        }

        // 2) Resolve range settings from the equipped weapon (fallback if none)
        WeaponRange weaponRange = ResolveWeaponRange();
        float minRange = Mathf.Max(weaponRange.minPreferredDistance, waypointTolerance * 0.5f);
        float maxRange = Mathf.Max(minRange, weaponRange.maxPreferredDistance);
        float preferredRange = Mathf.Clamp(weaponRange.preferredDistance, minRange, maxRange);

        float minRangeSqr = minRange * minRange;
        float maxRangeSqr = maxRange * maxRange;

        // 3) Basic geometry
        Vector3 currentPosition = CurrentPosition;
        Vector3 toTarget = intent.TargetLocationVector - currentPosition;
        toTarget.y = 0f;

        float distanceSqr = toTarget.sqrMagnitude;
        bool tooClose = distanceSqr < minRangeSqr;
        bool tooFar = distanceSqr > maxRangeSqr;
        bool withinDesiredRange = !tooClose && !tooFar;

        // 4) Always aim (even while moving)
        AimAtTarget(intent.TargetLocationTransform);

        // 5) Active stance is based on max range (your previous behaviour)
        var isActiveStance = distanceSqr <= maxRangeSqr;
        combatActions?.SetActiveStance(isActiveStance);

        bool aimedAtTarget = isActiveStance
            && combatActions != null
            && intent.TargetLocationTransform
            && combatActions.IsFacingTarget(intent.TargetLocationTransform, facingThresholdDegrees);

        if (aimedAtTarget && !HasFriendlyInLineOfFire(intent))
            combatActions.ActivateEquippedItem();

        // 6) Decide whether to rebuild path:
        //    - If intent changed -> rebuild
        //    - If we're not in range:
        //        - If we have no path -> rebuild
        //        - If our path endpoint would still not be in range -> rebuild
        bool intentChanged = IntentChanged(intent, intent.TargetLocationVector, preferredRange);

        bool hasPath = path != null && path.Length > 0;
        bool hasEnd = hasPath && path.Length > 0;

        // Convert last path node to world (assumes Vector2 is world XZ; adjust if it's grid)
        Vector3 pathEndWorld = hasEnd
            ? new Vector3(path[^1].x, currentPosition.y, path[^1].y)
            : Vector3.zero;

        bool endWithinDesiredRange = false;
        if (hasEnd)
        {
            Vector3 endToTarget = intent.TargetLocationVector - pathEndWorld;
            endToTarget.y = 0f;
            float endDistSqr = endToTarget.sqrMagnitude;
            endWithinDesiredRange = endDistSqr >= minRangeSqr && endDistSqr <= maxRangeSqr;
        }

        bool shouldRepath =
            intentChanged ||
            (!withinDesiredRange && (!hasPath || !endWithinDesiredRange));

        if (shouldRepath)
        {
            RebuildPath(intent, intent.TargetLocationVector, preferredRange);
            pathIndex = 0;
        }

        // 8) Otherwise follow path if we have one
        if (path == null || path.Length == 0)
            return;

        pathIndex = motorActions.MoveToPathPosition(
            currentPosition,
            path,
            pathIndex,
            !isActiveStance,
            sprintToTarget,
            waypointTolerance
        );
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

    private void AimAtTarget(Transform targetTransform)
    {
        if (targetTransform)
            motorActions.RotateToTarget(ResolveAimTransform(targetTransform));
    }

    private WeaponRange ResolveWeaponRange()
    {
        if (combatActions?.EquippedWeapon is IWeapon weapon)
            return weapon.WeaponRange;

        return fallbackWeaponRange;
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

    private static bool IsTargetDead(EngageIntent intent)
    {
        var targetKnowledge = intent?.Target;
        return targetKnowledge?.IsBelievedDead == true;
    }

    private bool HasFriendlyInLineOfFire(EngageIntent intent)
    {
        if (knowledge == null || knowledge.Self == null)
            return false;

        var selfFactionBelief = knowledge.Self.FactionId;
        if (!selfFactionBelief.HasValue)
            return false;

        string selfFaction = selfFactionBelief.Value.Value;
        if (string.IsNullOrWhiteSpace(selfFaction))
            return false;

        Transform targetTransform = intent?.TargetLocationTransform;
        if (!targetTransform)
            return false;

        Transform aimTransform = ResolveAimTransform(targetTransform);
        Vector3 start = CurrentPosition;
        Vector3 end = aimTransform ? aimTransform.position : targetTransform.position;

        foreach (var kvp in knowledge.Characters)
        {
            var character = kvp.Value;
            if (character == null)
                continue;

            if(character.IsBelievedDead)
                continue;

            if (character.Id == knowledge.Self.Id)
                continue;

            if (!string.IsNullOrWhiteSpace(intent?.TargetId) && character.Id == intent.TargetId)
                continue;

            var factionBelief = character.FactionId;
            if (!factionBelief.HasValue)
                continue;

            string factionId = factionBelief.Value.Value;
            if (string.IsNullOrWhiteSpace(factionId) || factionId != selfFaction)
                continue;

            if (!character.Position.HasValue)
                continue;

            Vector3 friendlyPosition = character.Position.Value.Value.Vector;
            float radius = ResolveCharacterRadius(character.CharacterObject);
            if (DistanceToSegmentSquared(friendlyPosition, start, end) <= radius * radius)
                return true;
        }

        return false;
    }

    private static float DistanceToSegmentSquared(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector3 segment = end - start;
        float lengthSqr = segment.sqrMagnitude;

        if (lengthSqr <= 0.0001f)
            return Vector3.SqrMagnitude(point - start);

        float t = Vector3.Dot(point - start, segment) / lengthSqr;
        t = Mathf.Clamp01(t);

        Vector3 projection = start + segment * t;
        projection.y = point.y;

        return Vector3.SqrMagnitude(point - projection);
    }

    private static float ResolveCharacterRadius(GameObject characterObject)
    {
        if (!characterObject)
            return 0.5f;

        var controller = characterObject.GetComponentInChildren<CharacterController>();
        if (controller)
            return Mathf.Max(0.5f, controller.radius);

        return 0.5f;
    }
}
