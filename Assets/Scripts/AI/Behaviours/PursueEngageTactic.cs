using UnityEngine;

public sealed class PursueEngageTactic : EngageTacticBehaviour
{
    [Header("Desired Range")]
    [SerializeField, Min(0f)] private float minDesiredRange = 1.5f;
    [SerializeField, Min(0f)] private float preferredDistance = 3.5f;
    [SerializeField, Min(0f)] private float maxDesiredRange = 6f;
    [SerializeField] private bool useCover = false;
    [SerializeField, Range(0f, 2f)] private float cautiousRangeMultiplier = 1.1f;

    public override EngageTactic TacticType => EngageTactic.Pursue;

    internal float DefaultMinRange => minDesiredRange;
    internal float DefaultPreferredDistance => preferredDistance;
    internal float DefaultMaxRange => maxDesiredRange;
    internal bool DefaultUseCover => useCover;
    internal float DefaultCautiousRangeMultiplier => cautiousRangeMultiplier;

    public override void Tick(EngageIntent intent, EngageTactics tacticsData, Vector3 targetPosition, Transform targetTransform)
    {
        if (behaviour == null || intent == null)
            return;

        var motor = behaviour.MotorActions;
        if (motor == null)
            return;

        var currentPosition = behaviour.CurrentPosition;
        Vector3 toTarget = targetPosition - currentPosition;
        toTarget.y = 0f;

        var pursueIntent = tacticsData?.Pursue;
        float minRange = Mathf.Max(ResolveValue(pursueIntent?.MinDesiredRange, minDesiredRange), behaviour.WaypointTolerance * 0.5f);
        float maxRange = Mathf.Max(minRange, ResolveValue(pursueIntent?.MaxDesiredRange, maxDesiredRange));
        float preferredRange = Mathf.Clamp(ResolveValue(pursueIntent?.PreferredDistance, preferredDistance), minRange, maxRange);
        bool useIntentCover = pursueIntent?.UseCover ?? useCover;

        if (useIntentCover)
        {
            minRange *= cautiousRangeMultiplier;
            maxRange *= cautiousRangeMultiplier;
            preferredRange *= cautiousRangeMultiplier;
        }

        bool intentChanged = behaviour.IntentChanged(intent, targetPosition, preferredRange);
        if (intentChanged)
        {
            behaviour.RebuildPath(intent, targetPosition, preferredRange);
            behaviour.CurrentPathIndex = 0;
        }

        float distanceSqr = toTarget.sqrMagnitude;
        bool withinActiveStanceRange = distanceSqr <= maxRange * maxRange;
        bool withinDesiredRange = distanceSqr >= minRange * minRange && distanceSqr <= maxRange * maxRange;

        if (behaviour.CombatActions)
            behaviour.CombatActions.SetActiveStance(withinActiveStanceRange);

        if (withinDesiredRange)
        {
            motor.MoveToPosition(currentPosition, targetPosition, true, false, Mathf.Max(minRange, behaviour.WaypointTolerance * 0.5f));
            behaviour.ClearPath();
            if (targetTransform)
                motor.RotateToTarget(ResolveAimTransform(targetTransform));
            else
                motor.AimFromPosition(currentPosition, toTarget);
            return;
        }

        var path = behaviour.CurrentPath;
        if (path == null || path.Length == 0)
            behaviour.RebuildPath(intent, targetPosition, preferredRange);

        path = behaviour.CurrentPath;
        if (path == null || path.Length == 0)
            return;

        behaviour.CurrentPathIndex = motor.MoveToPathPosition(currentPosition, path, behaviour.CurrentPathIndex, behaviour.FaceTargetWhileMoving, behaviour.SprintToTarget, behaviour.WaypointTolerance);
        if (targetTransform)
            motor.RotateToTarget(ResolveAimTransform(targetTransform));
        else
            motor.AimFromPosition(currentPosition, toTarget);
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
