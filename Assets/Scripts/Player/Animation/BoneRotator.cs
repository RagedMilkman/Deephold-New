using System;
using UnityEngine;

internal sealed class BoneRotator
{
    internal readonly struct BoneRotationResult
    {
        internal bool Applied { get; }
        internal bool WasRestricted { get; }
        internal bool ExceededComfort { get; }
        internal bool ShouldAskParent { get; }

        private BoneRotationResult(bool applied, bool wasRestricted, bool exceededComfort, bool shouldAskParent)
        {
            Applied = applied;
            WasRestricted = wasRestricted;
            ExceededComfort = exceededComfort;
            ShouldAskParent = shouldAskParent;
        }

        internal static BoneRotationResult AppliedResult(bool wasRestricted, bool exceededComfort, bool forceParent = false, bool smoothingLimited = false)
        {
            bool requestParent = forceParent || wasRestricted || exceededComfort || smoothingLimited;
            return new BoneRotationResult(true, wasRestricted, exceededComfort, requestParent);
        }

        internal static BoneRotationResult NotApplied(bool requestParent)
        {
            return new BoneRotationResult(false, false, false, requestParent);
        }
    }

    private readonly HumanoidRigAnimator owner;
    private readonly HumanBodyBones bone;
    private readonly string label;
    private readonly Func<bool> isEnabled;
    private readonly Func<float> restrictYawLimitProvider;
    private readonly Func<float> restrictPitchLimitProvider;
    private readonly Func<float> comfortableYawLimitProvider;
    private readonly Func<float> comfortablePitchLimitProvider;
    private readonly Func<bool> shouldDrawComfortRange;
    private readonly BasisBuilder basisBuilder;
    private readonly Color debugColor;
    private readonly bool restoreOnFailure;
    private readonly Func<float> yawSpeedProvider;
    private readonly Func<float> pitchSpeedProvider;

    private const float ResidualRotationThreshold = 0.5f;
    private const float ComfortHysteresisDegrees = 1f;

    private bool outOfComfortRange;
    private bool hasSmoothedAngles;
    private float currentYaw;
    private float currentPitch;

    internal delegate bool BasisBuilder(HumanoidRigAnimator.BonePose pose, out Vector3 forward, out Vector3 up, out Vector3 right);

    internal BoneRotator(
        HumanoidRigAnimator owner,
        HumanBodyBones bone,
        string label,
        Func<bool> isEnabled,
        Func<float> restrictYawLimitProvider,
        Func<float> restrictPitchLimitProvider,
        Func<float> comfortableYawLimitProvider,
        Func<float> comfortablePitchLimitProvider,
        Func<bool> shouldDrawComfortRange,
        BasisBuilder basisBuilder,
        Color debugColor,
        bool restoreOnFailure,
        Func<float> yawSpeedProvider,
        Func<float> pitchSpeedProvider)
    {
        this.owner = owner;
        this.bone = bone;
        this.label = label;
        this.isEnabled = isEnabled;
        this.restrictYawLimitProvider = restrictYawLimitProvider;
        this.restrictPitchLimitProvider = restrictPitchLimitProvider;
        this.comfortableYawLimitProvider = comfortableYawLimitProvider;
        this.comfortablePitchLimitProvider = comfortablePitchLimitProvider;
        this.shouldDrawComfortRange = shouldDrawComfortRange;
        this.basisBuilder = basisBuilder;
        this.debugColor = debugColor;
        this.restoreOnFailure = restoreOnFailure;
        this.yawSpeedProvider = yawSpeedProvider;
        this.pitchSpeedProvider = pitchSpeedProvider;
    }

    internal BoneRotationResult Update(Vector3 targetPosition)
    {
        if (!isEnabled())
        {
            owner.RestoreBoneDefaultPose(bone);
            outOfComfortRange = false;
            hasSmoothedAngles = false;
            currentYaw = 0f;
            currentPitch = 0f;
            return BoneRotationResult.NotApplied(requestParent: true);
        }

        if (!owner.TryGetBonePose(bone, out var pose) || pose.Transform == null)
        {
            owner.CacheBones();
            hasSmoothedAngles = false;
            currentYaw = 0f;
            currentPitch = 0f;
            return BoneRotationResult.NotApplied(requestParent: true);
        }

        if (!basisBuilder(pose, out var forward, out var up, out var right))
        {
            if (restoreOnFailure)
            {
                owner.RestoreBoneDefaultPose(bone);
                outOfComfortRange = false;
                hasSmoothedAngles = false;
                currentYaw = 0f;
                currentPitch = 0f;
            }

            return BoneRotationResult.NotApplied(requestParent: true);
        }

        if (!HumanoidRigAnimator.TryComputeYawPitch(pose, targetPosition, forward, up, right, out float yaw, out float pitch))
        {
            if (restoreOnFailure)
            {
                owner.RestoreBoneDefaultPose(bone);
                outOfComfortRange = false;
                hasSmoothedAngles = false;
                currentYaw = 0f;
                currentPitch = 0f;
            }

            return BoneRotationResult.NotApplied(requestParent: true);
        }

        float restrictYaw = Mathf.Max(0f, restrictYawLimitProvider());
        float restrictPitch = Mathf.Max(0f, restrictPitchLimitProvider());
        float comfortableYaw = Mathf.Clamp(Mathf.Max(0f, comfortableYawLimitProvider()), 0f, restrictYaw);
        float comfortablePitch = Mathf.Clamp(Mathf.Max(0f, comfortablePitchLimitProvider()), 0f, restrictPitch);

        float enterYawComfort = comfortableYaw + ComfortHysteresisDegrees;
        float enterPitchComfort = comfortablePitch + ComfortHysteresisDegrees;
        float exitYawComfort = Mathf.Max(0f, comfortableYaw - ComfortHysteresisDegrees);
        float exitPitchComfort = Mathf.Max(0f, comfortablePitch - ComfortHysteresisDegrees);

        bool desiredExceedsComfort = outOfComfortRange
            ? IsBeyondComfort(yaw, pitch, exitYawComfort, exitPitchComfort)
            : IsBeyondComfort(yaw, pitch, enterYawComfort, enterPitchComfort);

        float clampedYaw = Mathf.Clamp(yaw, -restrictYaw, restrictYaw);
        float clampedPitch = Mathf.Clamp(pitch, -restrictPitch, restrictPitch);
        bool wasRestricted = Mathf.Abs(clampedYaw - yaw) > 0.01f || Mathf.Abs(clampedPitch - pitch) > 0.01f;

        if (!hasSmoothedAngles)
        {
            currentYaw = 0f;
            currentPitch = 0f;
            hasSmoothedAngles = true;
        }

        float yawSpeed = Mathf.Max(0f, yawSpeedProvider());
        float pitchSpeed = Mathf.Max(0f, pitchSpeedProvider());
        float maxYawDelta = yawSpeed * Time.deltaTime;
        float maxPitchDelta = pitchSpeed * Time.deltaTime;

        float targetYaw = clampedYaw;
        float targetPitch = clampedPitch;

        float newYaw = yawSpeed <= 0f
            ? targetYaw
            : HumanoidRigAnimator.MoveAngleTowards(currentYaw, targetYaw, maxYawDelta);
        float newPitch = pitchSpeed <= 0f
            ? targetPitch
            : HumanoidRigAnimator.MoveAngleTowards(currentPitch, targetPitch, maxPitchDelta);

        currentYaw = newYaw;
        currentPitch = newPitch;

        HumanoidRigAnimator.ApplyBoneYawPitch(pose, currentYaw, currentPitch, forward, up, right);

        bool appliedExceedsComfort = outOfComfortRange
            ? IsBeyondComfort(currentYaw, currentPitch, exitYawComfort, exitPitchComfort)
            : IsBeyondComfort(currentYaw, currentPitch, enterYawComfort, enterPitchComfort);
        bool smoothingLimited = (wasRestricted || desiredExceedsComfort)
            && (Mathf.Abs(Mathf.DeltaAngle(currentYaw, targetYaw)) > 0.01f
                || Mathf.Abs(Mathf.DeltaAngle(currentPitch, targetPitch)) > 0.01f);
        bool exceedsComfort = desiredExceedsComfort || appliedExceedsComfort;

        if (desiredExceedsComfort && !outOfComfortRange)
        {
            outOfComfortRange = true;
        }
        else if (!desiredExceedsComfort)
        {
            outOfComfortRange = false;
        }

        if (shouldDrawComfortRange())
        {
            HumanoidRigDebugVisualizer.DrawComfortRange(
                pose,
                forward,
                up,
                right,
                comfortableYaw,
                comfortablePitch,
                owner.ComfortRangeDebugLength,
                debugColor);
        }

        bool forceParentRotation = owner.ShouldForceParentRotation;

        if (forceParentRotation)
        {

        }

        return BoneRotationResult.AppliedResult(wasRestricted, exceedsComfort, forceParentRotation, smoothingLimited);
    }

    internal bool HasResidualRotation
    {
        get
        {
            if (!hasSmoothedAngles)
            {
                return false;
            }

            return Mathf.Abs(currentYaw) > ResidualRotationThreshold
                || Mathf.Abs(currentPitch) > ResidualRotationThreshold;
        }
    }

    internal void Reset()
    {
        outOfComfortRange = false;
        hasSmoothedAngles = false;
        currentYaw = 0f;
        currentPitch = 0f;
    }

    private static bool IsBeyondComfort(float yaw, float pitch, float comfortableYaw, float comfortablePitch)
    {
        return Mathf.Abs(yaw) > comfortableYaw || Mathf.Abs(pitch) > comfortablePitch;
    }
}
