using System;
using System.Collections.Generic;
using UnityEngine;
using RootMotion.FinalIK;

/// <summary>
/// Humanoid rig helper that exposes simple controls for driving debug spins and head look direction.
/// </summary>
[DisallowMultipleComponent]
public class HumanoidRigAnimator : MonoBehaviour
{
    internal struct BonePose
    {
        public Transform Transform;
        public Quaternion DefaultLocalRotation;
    }

    private sealed class BoneChainNode
    {
        internal BoneChainNode(
            BoneChainNode parent,
            Func<Vector3, BoneRotator.BoneRotationResult> rotate,
            Action restoreDefaultPose,
            Action resetState,
            Func<bool> hasResidualRotation,
            HumanBodyBones? associatedBone)
        {
            Parent = parent;
            Rotate = rotate;
            RestoreDefaultPose = restoreDefaultPose;
            ResetState = resetState;
            HasResidualRotation = hasResidualRotation;
            AssociatedBone = associatedBone;
        }

        internal BoneChainNode Parent { get; }
        internal Func<Vector3, BoneRotator.BoneRotationResult> Rotate { get; }
        internal Action RestoreDefaultPose { get; }
        internal Action ResetState { get; }
        internal Func<bool> HasResidualRotation { get; }
        internal HumanBodyBones? AssociatedBone { get; }
    }

    private enum BoneAxisOption
    {
        PositiveX,
        NegativeX,
        PositiveY,
        NegativeY,
        PositiveZ,
        NegativeZ
    }

    [Header("Debug Spins")]
    [SerializeField] private bool debugSpinArms;
    [SerializeField] private bool debugSpinLegs;
    [SerializeField] private bool debugSpinTorso;
    [SerializeField] private bool debugSpinHead;
    [SerializeField] [Min(0f)] private float debugSpinSpeed = 180f;

    [Header("Look Rotation Options")]
    [SerializeField] private bool enableHeadRotation = true;
    [SerializeField] private bool enableSpineRotation = true;
    [SerializeField] private bool enableCharacterRotation = true;

    [Header("Head Target Smoothing")]
    [SerializeField] [Min(0f)] private float headTargetLatitudeSpeed = 90f;
    [SerializeField] [Min(0f)] private float headTargetLongitudeSpeed = 135f;

    [Header("Head Rotation Smoothing")]
    [SerializeField] [Min(0f)] private float headYawSpeed = 360f;
    [SerializeField] [Min(0f)] private float headPitchSpeed = 360f;

    [Header("Chest Target Offset")]
    [SerializeField] [Min(0f)] private float chestTargetOffsetDistance = 0f;
    [SerializeField] private float chestTargetVerticalOffsetDistance = 0f;

    [Header("Head Rotation Limits")]
    [SerializeField] [Min(0f)] private float headYawRestrictLimit = 70f;
    [SerializeField] [Min(0f)] private float headPitchRestrictLimit = 50f;

    [Header("Head Comfort Range")]
    [SerializeField] [Min(0f)] private float comfortableHeadYawLimit = 35f;
    [SerializeField] [Min(0f)] private float comfortableHeadPitchLimit = 25f;

    [Header("Head Axis Configuration")]
    [SerializeField] private BoneAxisOption headYawAxis = BoneAxisOption.NegativeZ;
    [SerializeField] private BoneAxisOption headPitchAxis = BoneAxisOption.PositiveX;

    [Header("Spine Rotation Limits")]
    [SerializeField] [Min(0f)] private float spineYawRestrictLimit = 40f;
    [SerializeField] [Min(0f)] private float spinePitchRestrictLimit = 25f;

    [Header("Spine Comfort Range")]
    [SerializeField] [Min(0f)] private float comfortableSpineYawLimit = 20f;
    [SerializeField] [Min(0f)] private float comfortableSpinePitchLimit = 12.5f;

    [Header("Spine Rotation Smoothing")]
    [SerializeField] [Min(0f)] private float spineYawSpeed = 180f;
    [SerializeField] [Min(0f)] private float spinePitchSpeed = 180f;

    [Header("Spine Axis Configuration")]
    [SerializeField] private BoneAxisOption spineYawAxis = BoneAxisOption.NegativeZ;
    [SerializeField] private BoneAxisOption spinePitchAxis = BoneAxisOption.PositiveX;

    [Header("Debug Visualization")]
    [SerializeField] private bool drawHeadComfortRange = true;
    [SerializeField] private bool drawSpineComfortRange = true;
    [SerializeField] private bool drawChestTarget = false;
    [SerializeField] [Min(0f)] private float comfortRangeDebugLength = 0.5f;

    [Header("Character Rotation Smoothing")]
    [SerializeField] [Min(0f)] private float characterYawSpeed = 720f;

    [Header("Final IK Integration")]
    [SerializeField] private BipedIK bipedIk;
    [SerializeField] private Transform characterYawTransform;
    [SerializeField] private Transform leftHandTarget;
    [SerializeField] private Transform rightHandTarget;
    [SerializeField] [Range(0f, 1f)] private float handPositionWeight = 1f;
    [SerializeField] [Range(0f, 1f)] private float handRotationWeight = 1f;

    private Animator humanoidAnimator;
    private bool bonesReady;

    private readonly Dictionary<HumanBodyBones, BonePose> bonePoses = new();

    private TopDownMotor.Stance currentStance = TopDownMotor.Stance.Passive;
    private TopDownMotor.MovementType currentMovementType = TopDownMotor.MovementType.Standing;

    private static readonly HumanBodyBones[] RequiredBones =
    {
        HumanBodyBones.LeftUpperArm,
        HumanBodyBones.RightUpperArm,
        HumanBodyBones.LeftLowerArm,
        HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftUpperLeg,
        HumanBodyBones.RightUpperLeg,
        HumanBodyBones.LeftLowerLeg,
        HumanBodyBones.RightLowerLeg,
        HumanBodyBones.Spine,
        HumanBodyBones.Chest,
        HumanBodyBones.Head
    };

    private bool hasHeadLookTarget;
    private Vector3 desiredHeadLookTarget;
    private Vector3 currentHeadLookTarget;
    private BoneRotator headRotator;
    private BoneRotator spineRotator;
    private readonly List<BoneChainNode> boneChainNodes = new();
    private BoneChainNode boneChainLeaf;
    private BoneChainNode headChainNode;
    private BoneChainNode spineChainNode;
    private BoneChainNode playerChainNode;
    private bool boneChainDirty = true;
    private bool lastHeadRotationEnabled;
    private bool lastSpineRotationEnabled;
    private bool lastCharacterRotationEnabled;

    internal float ComfortRangeDebugLength => comfortRangeDebugLength;
    internal bool ShouldForceParentRotation =>
        currentStance == TopDownMotor.Stance.Active ||
        currentMovementType == TopDownMotor.MovementType.Moving ||
        currentMovementType == TopDownMotor.MovementType.Sprinting;

    public void SetStance(TopDownMotor.Stance stance)
    {
        currentStance = stance;
    }

    public void SetMovementType(TopDownMotor.MovementType movementType)
    {
        currentMovementType = movementType;
    }

    private void InitializeBoneRotators()
    {
        headRotator = new BoneRotator(
            this,
            HumanBodyBones.Head,
            "head",
            () => enableHeadRotation,
            () => headYawRestrictLimit,
            () => headPitchRestrictLimit,
            () => comfortableHeadYawLimit,
            () => comfortableHeadPitchLimit,
            () => drawHeadComfortRange,
            TryGetHeadBasis,
            Color.magenta,
            restoreOnFailure: false,
            () => headYawSpeed,
            () => headPitchSpeed);

        spineRotator = new BoneRotator(
            this,
            HumanBodyBones.Spine,
            "spine",
            () => enableSpineRotation,
            () => spineYawRestrictLimit,
            () => spinePitchRestrictLimit,
            () => comfortableSpineYawLimit,
            () => comfortableSpinePitchLimit,
            () => drawSpineComfortRange,
            TryGetSpineBasis,
            Color.green,
            restoreOnFailure: true,
            () => spineYawSpeed,
            () => spinePitchSpeed);

        MarkBoneChainDirty();
    }

    private void Awake()
    {
        CacheAnimator();
        CacheBipedIk();
        CacheBones();
        InitializeBoneRotators();
    }

    private void OnEnable()
    {
        RestoreDefaultPoses();
        ApplyHandTargets();
    }

    private void OnDisable()
    {
        RestoreDefaultPoses();
    }

    private void Reset()
    {
        CacheAnimator();
        CacheBipedIk();
        CacheBones();
        InitializeBoneRotators();
        RestoreDefaultPoses();
        ApplyHandTargets();
    }

    public void SetCharacterYawTransform(Transform yawTransform)
    {
        characterYawTransform = yawTransform ? yawTransform : transform.root;
    }

    public void SetHeadLookTarget(Vector3 worldPosition)
    {
        desiredHeadLookTarget = worldPosition;

        if (!hasHeadLookTarget)
        {
            currentHeadLookTarget = worldPosition;
        }

        hasHeadLookTarget = true;
    }

    public void ClearHeadLookTarget()
    {
        hasHeadLookTarget = false;
        desiredHeadLookTarget = Vector3.zero;
        currentHeadLookTarget = Vector3.zero;
        headRotator?.Reset();
        spineRotator?.Reset();
        ResetCharacterYawSmoothing();
        RestoreBoneDefaultPose(HumanBodyBones.Head);
        RestoreBoneDefaultPose(HumanBodyBones.Spine);
    }

    private void CacheAnimator()
    {
        humanoidAnimator = GetComponentInChildren<Animator>();
        if (humanoidAnimator == null)
        {
            Debug.LogWarning($"{nameof(HumanoidRigAnimator)} requires a humanoid Animator in a child of '{name}'.", this);
            bonesReady = false;
            bonePoses.Clear();
            return;
        }

        if (!humanoidAnimator.isHuman)
        {
            Debug.LogWarning($"Animator on '{humanoidAnimator.name}' is not set up as Humanoid. Unable to procedurally animate bones.", this);
            bonesReady = false;
            bonePoses.Clear();
            return;
        }
    }

    internal void CacheBones()
    {
        if (humanoidAnimator == null || !humanoidAnimator.isHuman)
        {
            bonesReady = false;
            return;
        }

        bonePoses.Clear();
        foreach (var bone in RequiredBones)
        {
            var transform = humanoidAnimator.GetBoneTransform(bone);
            if (transform == null)
            {
                Debug.LogWarning($"Could not find bone '{bone}' on '{humanoidAnimator.name}'.", this);
                continue;
            }

            bonePoses[bone] = new BonePose
            {
                Transform = transform,
                DefaultLocalRotation = transform.localRotation
            };
        }

        bonesReady = bonePoses.Count > 0;
    }

    public void ApplyHandTargets(Transform leftTarget = null, Transform rightTarget = null)
    {
        leftHandTarget = leftTarget;
        rightHandTarget = rightTarget;
        ApplyHandEffectors();
    }

    private void CacheBipedIk()
    {
        if (bipedIk != null)
            return;

        bipedIk = GetComponentInChildren<BipedIK>();
    }

    private void LateUpdate()
    {
        if (!bonesReady)
        {
            CacheAnimator();
            CacheBones();
            return;
        }

        ApplyHandEffectors();

        if (debugSpinArms)
        {
            ApplySpin(HumanBodyBones.LeftUpperArm, Vector3.up);
            ApplySpin(HumanBodyBones.RightUpperArm, Vector3.up);
            ApplySpin(HumanBodyBones.LeftLowerArm, Vector3.up);
            ApplySpin(HumanBodyBones.RightLowerArm, Vector3.up);
        }

        if (debugSpinLegs)
        {
            ApplySpin(HumanBodyBones.LeftUpperLeg, Vector3.up);
            ApplySpin(HumanBodyBones.RightUpperLeg, Vector3.up);
            ApplySpin(HumanBodyBones.LeftLowerLeg, Vector3.up);
            ApplySpin(HumanBodyBones.RightLowerLeg, Vector3.up);
        }
        else
        {
            RestoreBoneDefaultPose(HumanBodyBones.LeftUpperLeg);
            RestoreBoneDefaultPose(HumanBodyBones.RightUpperLeg);
            RestoreBoneDefaultPose(HumanBodyBones.LeftLowerLeg);
            RestoreBoneDefaultPose(HumanBodyBones.RightLowerLeg);
        }

        if (debugSpinTorso)
        {
            ApplySpin(HumanBodyBones.Spine, Vector3.up);
            ApplySpin(HumanBodyBones.Chest, Vector3.up);
        }
        else
        {
            RestoreBoneDefaultPose(HumanBodyBones.Spine);
            RestoreBoneDefaultPose(HumanBodyBones.Chest);
        }

        if (debugSpinHead)
        {
            ApplySpin(HumanBodyBones.Head, Vector3.up);
        }
        else if (hasHeadLookTarget)
        {
            UpdateCurrentHeadLookTarget();
            ApplyHeadLookAtTarget();
        }
        else
        {
            RestoreBoneDefaultPose(HumanBodyBones.Head);
        }
    }

    private void ApplyHeadLookAtTarget()
    {
        if (headRotator == null || spineRotator == null)
        {
            InitializeBoneRotators();
        }

        RebuildBoneChainIfNeeded();

        if (boneChainLeaf == null)
        {
            return;
        }

        Vector3 chestTarget = GetChestTarget(currentHeadLookTarget);
        var usedNodes = new HashSet<BoneChainNode>();
        ProcessBoneChain(boneChainLeaf, currentHeadLookTarget, usedNodes);

        RestoreUnusedBones(usedNodes);

        if (drawChestTarget
            && spineChainNode != null
            && usedNodes.Contains(spineChainNode)
            && TryGetBonePose(HumanBodyBones.Chest, out var chestPose))
        {
            HumanoidRigDebugVisualizer.DrawChestTargetLine(chestPose, chestTarget);
        }

        if (headChainNode != null && usedNodes.Contains(headChainNode) && TryGetBonePose(HumanBodyBones.Head, out var headPose))
        {
            HumanoidRigDebugVisualizer.DrawHeadTargetLine(headPose, currentHeadLookTarget);
        }
    }

    private void ApplyHandEffectors()
    {
        if (bipedIk == null)
        {
            CacheBipedIk();
        }

        if (bipedIk == null)
        {
            return;
        }

        ApplyHandEffector(bipedIk.solvers.leftHand, leftHandTarget);
        ApplyHandEffector(bipedIk.solvers.rightHand, rightHandTarget);
    }

    private void ApplyHandEffector(IKSolverLimb solver, Transform target)
    {
        if (solver == null)
        {
            return;
        }

        solver.target = target;
        solver.IKPositionWeight = target && handPositionWeight > 0f ? handPositionWeight : 0f;
        solver.IKRotationWeight = target && handRotationWeight > 0f ? handRotationWeight : 0f;
    }

    private void ApplyRotation(HumanBodyBones bone, Quaternion offset)
    {
        if (!bonePoses.TryGetValue(bone, out var pose) || pose.Transform == null)
        {
            CacheBones();
            return;
        }

        pose.Transform.localRotation = pose.DefaultLocalRotation * offset;
    }

    private bool TryGetHeadBasis(BonePose pose, out Vector3 forward, out Vector3 up, out Vector3 right)
    {
        return TryGetConfiguredBasis(pose, headYawAxis, headPitchAxis, out forward, out up, out right);
    }

    private bool TryGetSpineBasis(BonePose pose, out Vector3 forward, out Vector3 up, out Vector3 right)
    {
        var transform = pose.Transform;
        if (transform != null && TryBuildHeadAlignedBasis(transform.parent, out forward, out up, out right))
        {
            return true;
        }

        return TryGetConfiguredBasis(pose, spineYawAxis, spinePitchAxis, out forward, out up, out right);
    }

    private Vector3 GetChestTarget(Vector3 targetPosition)
    {
        Vector3 chestTarget = targetPosition;

        if (chestTargetOffsetDistance > 0f
            && bonePoses.TryGetValue(HumanBodyBones.Chest, out var chestPose)
            && chestPose.Transform != null)
        {
            Vector3 chestPosition = chestPose.Transform.position;
            Vector3 direction = targetPosition - chestPosition;
            if (direction.sqrMagnitude >= 0.0001f)
            {
                float adjustedDistance = direction.magnitude + chestTargetOffsetDistance;
                chestTarget = chestPosition + direction.normalized * adjustedDistance;
            }
        }

        if (Mathf.Abs(chestTargetVerticalOffsetDistance) > 0f)
        {
            chestTarget += Vector3.up * chestTargetVerticalOffsetDistance;
        }

        return chestTarget;
    }

    private void UpdateCurrentHeadLookTarget()
    {
        if (!hasHeadLookTarget)
        {
            return;
        }

        if (!bonePoses.TryGetValue(HumanBodyBones.Head, out var headPose) || headPose.Transform == null)
        {
            UpdateHeadLookTargetLinearly();
            return;
        }

        Vector3 headPosition = headPose.Transform.position;
        Vector3 desiredOffset = desiredHeadLookTarget - headPosition;
        if (desiredOffset.sqrMagnitude < 0.0001f)
        {
            currentHeadLookTarget = desiredHeadLookTarget;
            return;
        }

        Vector3 currentOffset = currentHeadLookTarget - headPosition;
        if (currentOffset.sqrMagnitude < 0.0001f)
        {
            currentOffset = desiredOffset;
        }

        Vector3 desiredDirection = desiredOffset.normalized;
        Vector3 currentDirection = currentOffset.normalized;

        DirectionToLatLon(desiredDirection, out float desiredLatitude, out float desiredLongitude);
        DirectionToLatLon(currentDirection, out float currentLatitude, out float currentLongitude);

        float maxLatitudeDelta = headTargetLatitudeSpeed * Time.deltaTime;
        float maxLongitudeDelta = headTargetLongitudeSpeed * Time.deltaTime;

        float newLatitude = headTargetLatitudeSpeed <= 0f
            ? desiredLatitude
            : MoveAngleTowards(currentLatitude, desiredLatitude, maxLatitudeDelta);

        float newLongitude = headTargetLongitudeSpeed <= 0f
            ? desiredLongitude
            : MoveAngleTowards(currentLongitude, desiredLongitude, maxLongitudeDelta);

        Vector3 updatedDirection = LatLonToDirection(newLatitude, newLongitude);

        float desiredDistance = desiredOffset.magnitude;
        currentHeadLookTarget = headPosition + updatedDirection * desiredDistance;
    }

    private void UpdateHeadLookTargetLinearly()
    {
        currentHeadLookTarget = desiredHeadLookTarget;
    }

    private bool AdjustCharacterYaw(Vector3 targetPosition)
    {
        if (!enableCharacterRotation)
        {
            ResetCharacterYawSmoothing();
            return false;
        }

        if (characterYawTransform == null)
        {
            characterYawTransform = transform.root;
        }

        Vector3 toTarget = targetPosition - characterYawTransform.position;
        Vector3 flattenedTarget = new(toTarget.x, 0f, toTarget.z);
        Vector3 flattenedForward = new(characterYawTransform.forward.x, 0f, characterYawTransform.forward.z);

        if (flattenedTarget.sqrMagnitude < 0.0001f || flattenedForward.sqrMagnitude < 0.0001f)
        {
            ResetCharacterYawSmoothing();
            return false;
        }

        flattenedTarget.Normalize();
        flattenedForward.Normalize();

        float horizontalAngle = Vector3.SignedAngle(flattenedForward, flattenedTarget, Vector3.up);

        float totalComfortYaw = 0f;
        if (!ShouldForceParentRotation)
        {
            if (headChainNode != null)
            {
                totalComfortYaw += comfortableHeadYawLimit;
            }

            if (spineChainNode != null)
            {
                totalComfortYaw += comfortableSpineYawLimit;
            }
        }

        float clampedAngle = totalComfortYaw > 0f
            ? Mathf.Clamp(horizontalAngle, -totalComfortYaw, totalComfortYaw)
            : 0f;

        float characterYawDelta = horizontalAngle - clampedAngle;

        float yawSpeed = Mathf.Max(0f, characterYawSpeed);
        float maxDelta = yawSpeed * Time.deltaTime;
        float appliedDelta = yawSpeed <= 0f
            ? characterYawDelta
            : Mathf.Clamp(characterYawDelta, -maxDelta, maxDelta);

        if (Mathf.Abs(appliedDelta) > 0.01f)
        {
            characterYawTransform.Rotate(Vector3.up, appliedDelta, Space.World);
            return true;
        }

        return false;
    }

    private void ResetCharacterYawSmoothing()
    {
        // Character yaw smoothing is handled per-frame via speed clamping,
        // so there is no state to reset between updates.
    }

    internal static void ApplyBoneYawPitch(BonePose pose, float yaw, float pitch, Vector3 forward, Vector3 up, Vector3 right)
    {
        Quaternion yawRotation = Quaternion.AngleAxis(yaw, up);
        Quaternion pitchRotation = Quaternion.AngleAxis(pitch, right);
        pose.Transform.localRotation = pitchRotation * yawRotation * pose.DefaultLocalRotation;
    }

    internal static float MoveAngleTowards(float current, float target, float maxDegreesDelta)
    {
        float delta = Mathf.DeltaAngle(current, target);
        if (Mathf.Abs(delta) <= maxDegreesDelta)
        {
            return target;
        }

        return current + Mathf.Sign(delta) * maxDegreesDelta;
    }

    private static void DirectionToLatLon(Vector3 direction, out float latitude, out float longitude)
    {
        Vector3 normalized = direction.normalized;
        float clampedY = Mathf.Clamp(normalized.y, -1f, 1f);
        latitude = Mathf.Asin(clampedY) * Mathf.Rad2Deg;
        longitude = Mathf.Atan2(normalized.x, normalized.z) * Mathf.Rad2Deg;
    }

    private static Vector3 LatLonToDirection(float latitude, float longitude)
    {
        float latRad = latitude * Mathf.Deg2Rad;
        float lonRad = longitude * Mathf.Deg2Rad;
        float cosLat = Mathf.Cos(latRad);

        Vector3 direction = new(
            Mathf.Sin(lonRad) * cosLat,
            Mathf.Sin(latRad),
            Mathf.Cos(lonRad) * cosLat);

        if (direction.sqrMagnitude < 0.0001f)
        {
            return Vector3.forward;
        }

        return direction.normalized;
    }

    internal static bool TryComputeYawPitch(
        BonePose pose,
        Vector3 targetPosition,
        Vector3 forward,
        Vector3 up,
        Vector3 right,
        out float yaw,
        out float pitch)
    {
        yaw = 0f;
        pitch = 0f;

        var boneTransform = pose.Transform;
        var parent = boneTransform.parent;
        if (parent == null)
        {
            return false;
        }

        Vector3 toTarget = targetPosition - boneTransform.position;
        if (toTarget.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        Vector3 localTargetDir = parent.InverseTransformDirection(toTarget.normalized);
        Vector3 targetForward = localTargetDir;
        if (targetForward.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        targetForward.Normalize();

        Vector3 projectedDefaultForward = Vector3.ProjectOnPlane(forward, up);
        Vector3 projectedTargetForward = Vector3.ProjectOnPlane(targetForward, up);
        if (projectedDefaultForward.sqrMagnitude < 0.0001f || projectedTargetForward.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        projectedDefaultForward.Normalize();
        projectedTargetForward.Normalize();

        yaw = Vector3.SignedAngle(projectedDefaultForward, projectedTargetForward, up);
        Quaternion yawRotation = Quaternion.AngleAxis(yaw, up);

        Vector3 yawAlignedForward = yawRotation * forward;
        Vector3 projectedYawAlignedForward = Vector3.ProjectOnPlane(yawAlignedForward, right);
        Vector3 projectedTargetPitch = Vector3.ProjectOnPlane(targetForward, right);

        if (projectedYawAlignedForward.sqrMagnitude < 0.0001f || projectedTargetPitch.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        projectedYawAlignedForward.Normalize();
        projectedTargetPitch.Normalize();

        pitch = Vector3.SignedAngle(projectedYawAlignedForward, projectedTargetPitch, right);
        return true;
    }

    private bool TryBuildHeadAlignedBasis(Transform targetParent, out Vector3 forward, out Vector3 up, out Vector3 right)
    {
        const float epsilon = 0.0001f;

        forward = Vector3.zero;
        up = Vector3.zero;
        right = Vector3.zero;

        if (targetParent == null)
        {
            return false;
        }

        if (!bonePoses.TryGetValue(HumanBodyBones.Head, out var headPose) || headPose.Transform == null)
        {
            return false;
        }

        if (!TryGetConfiguredBasis(headPose, headYawAxis, headPitchAxis, out var headForward, out var headUp, out var headRight))
        {
            return false;
        }

        var headParent = headPose.Transform.parent;
        if (headParent == null)
        {
            return false;
        }

        Vector3 worldForward = headParent.TransformDirection(headForward);
        Vector3 worldUp = headParent.TransformDirection(headUp);
        Vector3 worldRight = headParent.TransformDirection(headRight);

        if (worldForward.sqrMagnitude < epsilon || worldUp.sqrMagnitude < epsilon || worldRight.sqrMagnitude < epsilon)
        {
            return false;
        }

        Vector3 localForward = targetParent.InverseTransformDirection(worldForward);
        Vector3 localUp = targetParent.InverseTransformDirection(worldUp);

        if (localForward.sqrMagnitude < epsilon || localUp.sqrMagnitude < epsilon)
        {
            return false;
        }

        localForward.Normalize();
        localUp = Vector3.ProjectOnPlane(localUp, localForward);

        if (localUp.sqrMagnitude < epsilon)
        {
            return false;
        }

        localUp.Normalize();
        Vector3 localRight = Vector3.Cross(localUp, localForward);

        if (localRight.sqrMagnitude < epsilon)
        {
            return false;
        }

        localRight.Normalize();
        localUp = Vector3.Cross(localForward, localRight).normalized;

        if (localUp.sqrMagnitude < epsilon)
        {
            return false;
        }

        forward = localForward;
        up = localUp;
        right = localRight;
        return true;
    }

    private bool TryGetConfiguredBasis(BonePose pose, BoneAxisOption yawAxisOption, BoneAxisOption pitchAxisOption, out Vector3 forward, out Vector3 up, out Vector3 right)
    {
        const float epsilon = 0.0001f;

        forward = Vector3.zero;
        up = Vector3.zero;
        right = Vector3.zero;

        Vector3 yawAxisLocal = AxisOptionToVector(yawAxisOption);
        Vector3 pitchAxisLocal = AxisOptionToVector(pitchAxisOption);

        if (yawAxisLocal.sqrMagnitude < epsilon || pitchAxisLocal.sqrMagnitude < epsilon)
        {
            return false;
        }

        Vector3 yawAxis = pose.DefaultLocalRotation * yawAxisLocal;
        Vector3 pitchAxis = pose.DefaultLocalRotation * pitchAxisLocal;

        if (yawAxis.sqrMagnitude < epsilon || pitchAxis.sqrMagnitude < epsilon)
        {
            return false;
        }

        yawAxis.Normalize();
        pitchAxis.Normalize();

        if (Mathf.Abs(Vector3.Dot(yawAxis, pitchAxis)) > 1f - epsilon)
        {
            return false;
        }

        Vector3 computedForward = Vector3.Cross(yawAxis, pitchAxis);
        if (computedForward.sqrMagnitude < epsilon)
        {
            return false;
        }

        computedForward.Normalize();

        Vector3 computedRight = Vector3.Cross(computedForward, yawAxis);
        if (computedRight.sqrMagnitude < epsilon)
        {
            return false;
        }

        computedRight.Normalize();

        if (Vector3.Dot(computedRight, pitchAxis) < 0f)
        {
            computedRight = -computedRight;
            computedForward = -computedForward;
        }

        forward = computedForward;
        up = yawAxis;
        right = computedRight;
        return true;
    }

    private static Vector3 AxisOptionToVector(BoneAxisOption option)
    {
        return option switch
        {
            BoneAxisOption.PositiveX => Vector3.right,
            BoneAxisOption.NegativeX => Vector3.left,
            BoneAxisOption.PositiveY => Vector3.up,
            BoneAxisOption.NegativeY => Vector3.down,
            BoneAxisOption.PositiveZ => Vector3.forward,
            BoneAxisOption.NegativeZ => Vector3.back,
            _ => Vector3.forward
        };
    }

    private void ApplySpin(HumanBodyBones bone, Vector3 axis)
    {
        if (debugSpinSpeed <= 0f)
        {
            return;
        }

        float angle = Time.time * debugSpinSpeed;
        ApplyRotation(bone, Quaternion.AngleAxis(angle, axis));
    }

    private void RestoreDefaultPoses()
    {
        foreach (var entry in bonePoses)
        {
            if (entry.Value.Transform != null)
            {
                entry.Value.Transform.localRotation = entry.Value.DefaultLocalRotation;
            }
        }

        hasHeadLookTarget = false;
        desiredHeadLookTarget = Vector3.zero;
        currentHeadLookTarget = Vector3.zero;
        headRotator?.Reset();
        spineRotator?.Reset();
        ResetCharacterYawSmoothing();
    }

    private void MarkBoneChainDirty()
    {
        boneChainDirty = true;
    }

    private void RebuildBoneChainIfNeeded()
    {
        bool headEnabled = enableHeadRotation;
        bool spineEnabled = enableSpineRotation;
        bool characterEnabled = enableCharacterRotation;

        if (!boneChainDirty && headEnabled == lastHeadRotationEnabled && spineEnabled == lastSpineRotationEnabled && characterEnabled == lastCharacterRotationEnabled)
        {
            return;
        }

        boneChainDirty = false;
        lastHeadRotationEnabled = headEnabled;
        lastSpineRotationEnabled = spineEnabled;
        lastCharacterRotationEnabled = characterEnabled;

        boneChainNodes.Clear();
        playerChainNode = null;
        spineChainNode = null;
        headChainNode = null;
        boneChainLeaf = null;

        BoneChainNode currentParent = null;

        if (characterEnabled)
        {
            playerChainNode = new BoneChainNode(
                parent: null,
                rotate: RotateCharacterNode,
                restoreDefaultPose: null,
                resetState: ResetCharacterYawSmoothing,
                hasResidualRotation: null,
                associatedBone: null);
            boneChainNodes.Add(playerChainNode);
            currentParent = playerChainNode;
        }
        else
        {
            ResetCharacterYawSmoothing();
        }

        if (spineRotator != null && spineEnabled)
        {
            spineChainNode = new BoneChainNode(
                parent: currentParent,
                rotate: target => spineRotator.Update(GetChestTarget(target)),
                restoreDefaultPose: () => RestoreBoneDefaultPose(HumanBodyBones.Spine),
                resetState: spineRotator.Reset,
                hasResidualRotation: () => spineRotator.HasResidualRotation,
                associatedBone: HumanBodyBones.Spine);
            boneChainNodes.Add(spineChainNode);
            currentParent = spineChainNode;
        }
        else if (spineRotator != null)
        {
            RestoreBoneDefaultPose(HumanBodyBones.Spine);
            spineRotator.Reset();
        }

        if (headRotator != null && headEnabled)
        {
            headChainNode = new BoneChainNode(
                parent: currentParent,
                rotate: target => headRotator.Update(target),
                restoreDefaultPose: () => RestoreBoneDefaultPose(HumanBodyBones.Head),
                resetState: headRotator.Reset,
                hasResidualRotation: () => headRotator.HasResidualRotation,
                associatedBone: HumanBodyBones.Head);
            boneChainNodes.Add(headChainNode);
            currentParent = headChainNode;
        }
        else if (headRotator != null)
        {
            RestoreBoneDefaultPose(HumanBodyBones.Head);
            headRotator.Reset();
        }

        boneChainLeaf = currentParent;
    }

    private BoneRotator.BoneRotationResult ProcessBoneChain(BoneChainNode node, Vector3 targetPosition, HashSet<BoneChainNode> usedNodes)
    {
        if (node == null)
        {
            return BoneRotator.BoneRotationResult.NotApplied(requestParent: false);
        }

        var result = node.Rotate(targetPosition);
        if (result.Applied)
        {
            usedNodes.Add(node);
        }

        bool parentNeeded = result.ShouldAskParent;

        if (!parentNeeded && node.Parent?.HasResidualRotation?.Invoke() == true)
        {
            parentNeeded = true;
        }

        if (parentNeeded && node.Parent != null)
        {
            ProcessBoneChain(node.Parent, targetPosition, usedNodes);

            result = node.Rotate(targetPosition);
            if (result.Applied)
            {
                usedNodes.Add(node);
            }
        }

        return result;
    }

    private void RestoreUnusedBones(HashSet<BoneChainNode> usedNodes)
    {
        foreach (var node in boneChainNodes)
        {
            if (!usedNodes.Contains(node))
            {
                node.RestoreDefaultPose?.Invoke();
                node.ResetState?.Invoke();
            }
        }
    }

    private BoneRotator.BoneRotationResult RotateCharacterNode(Vector3 targetPosition)
    {
        bool rotated = AdjustCharacterYaw(targetPosition);
        return rotated
            ? BoneRotator.BoneRotationResult.AppliedResult(wasRestricted: false, exceededComfort: false)
            : BoneRotator.BoneRotationResult.NotApplied(requestParent: false);
    }

    internal bool TryGetBonePose(HumanBodyBones bone, out BonePose pose)
    {
        return bonePoses.TryGetValue(bone, out pose);
    }

    internal void RestoreBoneDefaultPose(HumanBodyBones bone)
    {
        if (!bonePoses.TryGetValue(bone, out var pose) || pose.Transform == null)
        {
            return;
        }

        pose.Transform.localRotation = pose.DefaultLocalRotation;
    }
}
