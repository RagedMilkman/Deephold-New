using System.Collections.Generic;
using RootMotion.FinalIK;
using UnityEngine;

/// <summary>
/// Super lightweight visualizer for a biped's stepping circle.
/// It simply tracks four world-space points:
///     - current left
///     - current right
///     - desired left
///     - desired right
/// The script does not move the IK targets yet; it only keeps the
/// desired markers on a fixed circle around the character and draws
/// gizmos so we can inspect them in the Scene view.
/// </summary>
[ExecuteAlways]
public class BipedGaitController : MonoBehaviour
{
    private const float RestDistanceTolerance = 0.0001f;

    public enum FootSide
    {
        Left,
        Right
    }

    public enum FootPlantState
    {
        Planted,
        Stepping
    }

    public struct FootExitInfo
    {
        public FootSide foot;
        public float excessDistance;
    }
    [System.Serializable]
    public class Foot
    {
        [Header("IK Target (Current)")]
        public Transform currentTarget;

        [Header("Desired Marker (optional)")]
        [Tooltip("Optional transform used purely for visualizing the desired position.")]
        public Transform desiredMarker;

        [HideInInspector] public Vector3 currentPos;
        [HideInInspector] public Vector3 desiredPos;
        [HideInInspector] public Quaternion desiredRot;
        [HideInInspector] public bool initialized;
        [HideInInspector] public Vector3 defaultRestOffset;
        [HideInInspector] public bool hasDefaultRestOffset;
        [HideInInspector] public Vector3 stepStartPos;
        [HideInInspector] public float stepProgress;
        [Tooltip("Whether this foot is currently planted or mid-step.")]
        public FootPlantState plantState = FootPlantState.Planted;
    }

    [Header("Rig")]
    public Transform bodyRoot;

    [Header("Feet")]
    public Foot leftFoot = new Foot();
    public Foot rightFoot = new Foot();

    [Header("Circle")]
    public float circleRadius = 0.3f;
    [Tooltip("Smallest radius the stepping circle can shrink to when visualized.")]
    public float minCircleRadius = 0.2f;
    [Tooltip("Largest radius the stepping circle can grow to when visualized.")]
    public float maxCircleRadius = 0.45f;
    [Tooltip("Fallback radius used for emergency step checks; should be larger than the max circle.")]
    public float fallbackCircleRadius = 0.6f;
    [Tooltip("Speed (m/s) at which the circle reaches its maximum radius.")]
    public float speedForMaxCircle = 3f;
    [Tooltip("Smoothing time (seconds) for growing/shrinking the circle radius.")]
    public float circleRadiusSmoothTime = 0.2f;
    [Tooltip("Maximum forward offset applied to the stepping circle at max speed.")]
    public float circleOffsetMaxDistance = 0.4f;
    [Tooltip("Smoothing time (seconds) for adjusting the circle offset.")]
    public float circleOffsetSmoothTime = 0.2f;

    [Header("Stepping")]
    [Tooltip("Minimum speed (m/s) used when moving a foot toward its desired point.")]
    public float minStepSpeed = 0.6f;
    [Tooltip("Maximum speed (m/s) used when moving a foot toward its desired point as movement increases.")]
    public float maxStepSpeed = 1.4f;
    [Tooltip("Minimum arc height (meters) used for lifting the foot during a step.")]
    public float minStepArcHeight = 0.03f;
    [Tooltip("Maximum arc height (meters) used for lifting the foot during a step as movement increases.")]
    public float maxStepArcHeight = 0.07f;
    [Tooltip("Smoothing time (seconds) for adjusting step speed and arc height based on movement speed.")]
    public float stepDynamicsSmoothTime = 0.1f;

    [Header("Hip Adjust")]
    [Tooltip("Minimum vertical offset from the initial hip height when bobbing in sync with steps.")]
    public float hipMinOffset = -0.01f;
    [Tooltip("Maximum vertical offset from the initial hip height when bobbing in sync with steps.")]
    public float hipMaxOffset = 0.02f;
    [Tooltip("If disabled, the gait controller will not modify the body's Y position for hip bobbing.")]
    public bool adjustHipHeight = true;

    [Header("Rest Positions")]
    [Tooltip("Distance from the circle center to each foot's rest point. Set to 0 to use the model's default pose distance.")]
    public float restRadius = 0.2f;
    [Tooltip("Time (seconds) the character must remain still before feet are moved to their rest points.")]
    public float restDelay = 0.5f;
    [Tooltip("Delay (seconds) enforced between commanding each foot to move into its rest position.")]
    public float restFootStaggerDelay = 0.2f;
    [Tooltip("How far the circle center must travel in a single frame to be considered movement.")]
    public float movementThreshold = 0.005f;

    [Header("Ground")]
    [Tooltip("Y height used for runtime stepping and hip calculations.")]
    public float groundPlaneY = 0f;

    [Header("Visualization")]
    [Tooltip("Y height where the debug markers and circles are drawn (ground level).")]
    public float debugPlaneY = -1f;

    private Vector3 lastCenterPosition;
    private float lastMovementTime;
    private Vector3 lastMovementDirection = Vector3.forward;
    private float lastRestCommandTime = float.NegativeInfinity;
    private float currentPlanarSpeed;
    private float circleRadiusVelocity;
    private Vector3 circleOffsetVelocity;
    private Vector3 currentCircleOffset;
    private float currentStepSpeed;
    private float currentStepArcHeight;
    private float stepSpeedVelocity;
    private float stepArcVelocity;
    private float initialHipHeight;
    private bool hipHeightCaptured;

    private enum ControllerState
    {
        Standing,
        Moving
    }

    private bool autoAssignmentComplete;

    private void Update()
    {
        TryAutoAssignFootTargets();

        EnsureInitialized(leftFoot);
        EnsureInitialized(rightFoot);

        Vector3 baseCenter = FlattenToPlane(GetCircleCenter());
        TrackMovement(baseCenter);
        UpdateCircleRadius();
        UpdateCircleOffset();
        UpdateStepDynamics();

        Vector3 centerWithOffset = ApplyCircleOffset(baseCenter);
        ControllerState state = DetermineState();
        if (state == ControllerState.Standing)
        {
            HandleStandingState(centerWithOffset);
        }
        else
        {
            HandleMovingState(centerWithOffset);
        }

        ProgressFootTowardsDesired(leftFoot);
        ProgressFootTowardsDesired(rightFoot);

        UpdateHipHeight();
    }

    private void Awake()
    {
        hipHeightCaptured = false;
        autoAssignmentComplete = false;
        ResetFeet();
        ResetMovementTracking();
        circleRadius = Mathf.Max(minCircleRadius, 0f);
        circleRadiusVelocity = 0f;
        currentCircleOffset = Vector3.zero;
        circleOffsetVelocity = Vector3.zero;
        currentStepSpeed = Mathf.Max(minStepSpeed, 0f);
        currentStepArcHeight = Mathf.Max(minStepArcHeight, 0f);
        stepSpeedVelocity = 0f;
        stepArcVelocity = 0f;
    }

    private void OnEnable()
    {
        hipHeightCaptured = false;
        autoAssignmentComplete = false;
        ResetFeet();
        ResetMovementTracking();
        circleRadius = Mathf.Max(minCircleRadius, 0f);
        circleRadiusVelocity = 0f;
        currentCircleOffset = Vector3.zero;
        circleOffsetVelocity = Vector3.zero;
        currentStepSpeed = Mathf.Max(minStepSpeed, 0f);
        currentStepArcHeight = Mathf.Max(minStepArcHeight, 0f);
        stepSpeedVelocity = 0f;
        stepArcVelocity = 0f;
    }

    private void OnValidate()
    {
        autoAssignmentComplete = false;
        ResetFeet();
        TryAutoAssignFootTargets();
        hipHeightCaptured = false;
    }

    private void ResetFeet()
    {
        ResetFoot(leftFoot);
        ResetFoot(rightFoot);
    }

    private void ResetFoot(Foot foot)
    {
        if (foot == null)
            return;

        foot.initialized = false;
        foot.plantState = FootPlantState.Planted;
        foot.stepProgress = 1f;
        foot.stepStartPos = Vector3.zero;
        foot.currentPos = Vector3.zero;
        foot.desiredPos = Vector3.zero;
        foot.desiredRot = Quaternion.identity;
        foot.defaultRestOffset = Vector3.zero;
        foot.hasDefaultRestOffset = false;
    }

    private void CacheInitialHipHeight()
    {
        Transform hip = bodyRoot != null ? bodyRoot : transform;
        if (hip == null)
            return;

        initialHipHeight = hip.position.y;
        hipHeightCaptured = true;
    }

    private void TryAutoAssignFootTargets()
    {
        if (autoAssignmentComplete)
            return;

        Transform searchRoot = bodyRoot != null ? bodyRoot : transform;
        FullBodyBipedIK solver = searchRoot.GetComponentInChildren<FullBodyBipedIK>(true);
        if (solver == null)
        {
            return;
        }

        bool leftAssigned = AssignFootTargetFromFinalIk(leftFoot, FootSide.Left, solver);
        bool rightAssigned = AssignFootTargetFromFinalIk(rightFoot, FootSide.Right, solver);

        if (leftAssigned)
        {
            InitializeFoot(leftFoot);
        }
        
        if (rightAssigned)
        {
            InitializeFoot(rightFoot);
        }

        autoAssignmentComplete = AreFootTargetsAssigned();
        if (autoAssignmentComplete)
        {           
        }
        else
        {
            Debug.Log($"[{name}] Auto-assign: still waiting for missing foot target(s). (L={(IsFootAssigned(leftFoot) ? "OK" : "pending")}, R={(IsFootAssigned(rightFoot) ? "OK" : "pending")})");
        }
    }

    private bool AssignFootTargetFromFinalIk(Foot foot, FootSide side, FullBodyBipedIK solver)
    {
        if (foot == null || solver == null || solver.solver == null)
            return false;

        Transform footTarget = foot.currentTarget;
        IKEffector effector = GetFootEffector(solver, side);
        if (effector == null)
            return false;

        // If the foot already has a target transform, push that reference
        // into the solver so the IK chain follows the gait controller.
        if (footTarget != null)
        {
            if (effector.target == footTarget)
                return false;

            effector.target = footTarget;
            return true;
        }

        // Otherwise adopt the solver's existing target so the foot can
        // start driving it.
        if (effector.target != null)
        {
            foot.currentTarget = effector.target;
            return true;
        }

        return false;
    }

    private static IKEffector GetFootEffector(FullBodyBipedIK solver, FootSide side)
    {
        if (solver == null || solver.solver == null)
            return null;

        return side == FootSide.Left
            ? solver.solver.leftFootEffector
            : solver.solver.rightFootEffector;
    }

    private bool AreFootTargetsAssigned()
    {
        return IsFootAssigned(leftFoot) && IsFootAssigned(rightFoot);
    }

    private static bool IsFootAssigned(Foot foot)
    {
        if (foot == null)
            return false;

        return foot.currentTarget != null;
    }

    private void InitializeFoot(Foot foot)
    {
        if (foot == null)
            return;

        foot.initialized = true;

        Vector3 sourcePos;
        Quaternion sourceRot;

        if (foot.currentTarget != null)
        {
            sourcePos = foot.currentTarget.position;
            sourceRot = foot.currentTarget.rotation;
        }
        else if (bodyRoot != null)
        {
            sourcePos = bodyRoot.position;
            sourceRot = bodyRoot.rotation;
        }
        else
        {
            sourcePos = transform.position;
            sourceRot = transform.rotation;
        }

        Vector3 flattenedSource = FlattenToPlane(sourcePos);
        foot.currentPos = flattenedSource;
        foot.desiredPos = foot.currentPos;
        foot.desiredRot = sourceRot;
        foot.plantState = FootPlantState.Planted;
        foot.stepStartPos = foot.currentPos;
        foot.stepProgress = 1f;

        CacheDefaultRestOffset(foot, flattenedSource);

        ApplyDesiredMarker(foot);
    }

    private void LateUpdate()
    {
        ApplyDesiredMarker(leftFoot);
        ApplyDesiredMarker(rightFoot);
    }

    public Vector3 CalculateRestPoint(Foot foot)
    {
        Vector3 center = GetOffsetCircleCenter();
        return CalculateRestPointInternal(foot, center);
    }

    private FootExitInfo[] CheckCurrentTargetsOutsideCircle(Vector3 center, bool plantedOnly)
    {
        return EvaluateFootExit(center, circleRadius, plantedOnly);
    }

    private FootExitInfo[] CheckCurrentTargetsOutsideFallbackCircle(Vector3 center, bool plantedOnly)
    {
        return EvaluateFootExit(center, GetFallbackRadius(), plantedOnly);
    }

    private FootExitInfo[] EvaluateFootExit(Vector3 center, float radius, bool plantedOnly)
    {
        EnsureInitialized(leftFoot);
        EnsureInitialized(rightFoot);

        List<FootExitInfo> exits = new List<FootExitInfo>(2);
        TryAddFootExit(leftFoot, FootSide.Left, center, radius, exits, plantedOnly);
        TryAddFootExit(rightFoot, FootSide.Right, center, radius, exits, plantedOnly);

        return exits.ToArray();
    }

    private void TryAddFootExit(Foot foot, FootSide side, Vector3 center, float radius, List<FootExitInfo> exits, bool plantedOnly)
    {
        if (foot == null)
            return;

        if (plantedOnly && foot.plantState != FootPlantState.Planted)
            return;

        if (TryGetFootExcessDistance(foot, center, radius, out float excess))
        {
            exits.Add(new FootExitInfo
            {
                foot = side,
                excessDistance = excess
            });
        }
    }

    private bool TryGetFootExcessDistance(Foot foot, Vector3 center, float radius, out float excess)
    {
        excess = 0f;

        if (foot == null)
            return false;

        Vector3 footPos = FlattenToPlane(foot.currentPos);
        float distance = Vector3.Distance(center, footPos);
        excess = distance - radius;
        return excess > 0f;
    }

    private string FormatFootExitInfos(FootExitInfo[] infos)
    {
        if (infos == null || infos.Length == 0)
            return "None";

        List<string> parts = new List<string>(infos.Length);
        for (int i = 0; i < infos.Length; i++)
        {
            FootExitInfo info = infos[i];
            parts.Add($"{info.foot} (+{info.excessDistance:F3}m)");
        }

        return string.Join(", ", parts);
    }

    private void ApplyDesiredMarker(Foot foot)
    {
        if (foot == null || foot.desiredMarker == null)
            return;

        Vector3 flattened = FlattenToPlane(foot.desiredPos);
        foot.desiredMarker.position = flattened;
        foot.desiredMarker.rotation = foot.desiredRot;
    }

    private void ResetMovementTracking()
    {
        lastCenterPosition = FlattenToPlane(GetCircleCenter());
        lastMovementTime = Time.time;
        lastMovementDirection = GetForwardOnPlane();
    }

    private void TrackMovement(Vector3 currentCenter)
    {
        Vector3 displacement = currentCenter - lastCenterPosition;
        Vector3 planarDisplacement = FlattenVector(displacement);
        float distance = planarDisplacement.magnitude;
        float deltaTime = Mathf.Max(GetDeltaTime(), 0.0001f);

        currentPlanarSpeed = distance / deltaTime;

        if (distance > 0.0001f)
        {
            lastMovementDirection = planarDisplacement / distance;
        }

        if (distance > movementThreshold)
        {
            lastMovementTime = Time.time;
        }

        lastCenterPosition = currentCenter;
    }

    private void UpdateCircleRadius(bool smooth = true)
    {
        float minRadius = Mathf.Max(minCircleRadius, 0f);
        float maxRadius = Mathf.Max(maxCircleRadius, minRadius);
        float speedMax = Mathf.Max(speedForMaxCircle, 0.0001f);
        float t = Mathf.InverseLerp(0f, speedMax, currentPlanarSpeed);
        float targetRadius = Mathf.Lerp(minRadius, maxRadius, t);

        if (!smooth)
        {
            circleRadius = targetRadius;
            return;
        }

        float smoothTime = Mathf.Max(circleRadiusSmoothTime, 0.0001f);
        circleRadius = Mathf.SmoothDamp(circleRadius, targetRadius, ref circleRadiusVelocity, smoothTime);
    }

    private void UpdateCircleOffset(bool smooth = true)
    {
        Vector3 travel = GetTravelDirectionOnPlane();
        float maxOffset = Mathf.Max(circleOffsetMaxDistance, 0f);
        float speedMax = Mathf.Max(speedForMaxCircle, 0.0001f);
        float t = Mathf.InverseLerp(0f, speedMax, currentPlanarSpeed);
        float targetDistance = Mathf.Lerp(0f, maxOffset, t);
        Vector3 targetOffset = FlattenVector(travel) * targetDistance;

        if (!smooth)
        {
            currentCircleOffset = targetOffset;
            return;
        }

        float smoothTime = Mathf.Max(circleOffsetSmoothTime, 0.0001f);
        currentCircleOffset = Vector3.SmoothDamp(currentCircleOffset, targetOffset, ref circleOffsetVelocity, smoothTime);
    }

    private void UpdateStepDynamics(bool smooth = true)
    {
        float speedMax = Mathf.Max(speedForMaxCircle, 0.0001f);
        float t = Mathf.InverseLerp(0f, speedMax, currentPlanarSpeed);

        float targetStepSpeed = Mathf.Lerp(Mathf.Max(minStepSpeed, 0f), Mathf.Max(maxStepSpeed, 0f), t);
        float targetArcHeight = Mathf.Lerp(Mathf.Max(minStepArcHeight, 0f), Mathf.Max(maxStepArcHeight, 0f), t);

        if (!smooth)
        {
            currentStepSpeed = targetStepSpeed;
            currentStepArcHeight = targetArcHeight;
            return;
        }

        float smoothTime = Mathf.Max(stepDynamicsSmoothTime, 0.0001f);
        currentStepSpeed = Mathf.SmoothDamp(currentStepSpeed, targetStepSpeed, ref stepSpeedVelocity, smoothTime);
        currentStepArcHeight = Mathf.SmoothDamp(currentStepArcHeight, targetArcHeight, ref stepArcVelocity, smoothTime);
    }

    private void UpdateHipHeight()
    {
        if (!adjustHipHeight)
            return;

        EnsureHipHeightCached();

        Transform hip = bodyRoot != null ? bodyRoot : transform;
        if (hip == null || !hipHeightCaptured)
            return;

        float phase = Mathf.Clamp01(GetDominantStepPhase());
        float bobT = Mathf.Sin(phase * Mathf.PI);
        float offset = Mathf.Lerp(hipMinOffset, hipMaxOffset, bobT);

        Vector3 position = hip.position;
        position.y = initialHipHeight + offset;
        hip.position = position;
    }

    private float GetFallbackRadius()
    {
        float enforcedMax = Mathf.Max(maxCircleRadius, 0f);
        return Mathf.Max(fallbackCircleRadius, enforcedMax);
    }

    private ControllerState DetermineState()
    {
        return Time.time - lastMovementTime >= restDelay
            ? ControllerState.Standing
            : ControllerState.Moving;
    }

    private void HandleStandingState(Vector3 center)
    {
        SnapFeetToRest(center);
    }

    private void HandleMovingState(Vector3 center)
    {
        bool leftPlanted = IsFootPlanted(leftFoot);
        bool rightPlanted = IsFootPlanted(rightFoot);

        if (leftPlanted && rightPlanted)
        {
            // Wants to step: evaluate the normal circle only if both feet are
            // currently planted so we know which one should move next.
            FootExitInfo[] wantsToStep = CheckCurrentTargetsOutsideCircle(center, true);
            if (TrySelectPriorityFoot(wantsToStep, out Foot footToStep))
            {
                BeginFootStep(footToStep);
                CalculateDesiredTarget(footToStep, center);
            }
        }
        else
        {

        }

        // Needs to step: we evaluate the fallback circle even if only one foot
        // is planted, but we ignore feet that are already stepping.
        FootExitInfo[] needsToStep = CheckCurrentTargetsOutsideFallbackCircle(center, true);
        if (TrySelectPriorityFoot(needsToStep, out Foot urgentFoot))
        {
            BeginFootStep(urgentFoot);
            CalculateDesiredTarget(urgentFoot, center);
        }
    }

    private bool TrySelectPriorityFoot(FootExitInfo[] infos, out Foot selectedFoot)
    {
        selectedFoot = null;

        if (infos == null || infos.Length == 0)
            return false;

        FootExitInfo best = infos[0];
        for (int i = 1; i < infos.Length; i++)
        {
            if (infos[i].excessDistance > best.excessDistance)
            {
                best = infos[i];
            }
        }

        selectedFoot = GetFootBySide(best.foot);
        return selectedFoot != null;
    }

    private float GetCurrentTime()
    {
        return Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
    }

    private float GetDeltaTime()
    {
        if (Application.isPlaying)
            return Time.deltaTime;

        return Time.deltaTime;
    }

    private Foot GetFootBySide(FootSide side)
    {
        return side == FootSide.Left ? leftFoot : rightFoot;
    }

    private void SnapFeetToRest(Vector3 center)
    {
        if (IsFootStepping(leftFoot) || IsFootStepping(rightFoot))
        {
            return;
        }

        float staggerDelay = Mathf.Max(restFootStaggerDelay, 0f);
        float time = GetCurrentTime();
        if (staggerDelay > 0f && time - lastRestCommandTime < staggerDelay)
        {
            return;
        }

        Foot footToRest = SelectFootNeedingRest(center);
        if (footToRest != null)
        {
            MoveFootToRest(footToRest, center);
        }
    }

    private Foot SelectFootNeedingRest(Vector3 center)
    {
        Foot selected = null;
        float largestDistance = 0f;

        void ConsiderFoot(Foot foot)
        {
            if (!IsFootPlanted(foot))
                return;

            float restDistance = GetRestDistance(foot, center);
            if (restDistance <= RestDistanceTolerance)
                return;

            if (selected == null || restDistance > largestDistance)
            {
                selected = foot;
                largestDistance = restDistance;
            }
        }

        ConsiderFoot(leftFoot);
        ConsiderFoot(rightFoot);

        return selected;
    }

    private float GetRestDistance(Foot foot, Vector3 center)
    {
        if (foot == null)
            return 0f;

        Vector3 restPoint = CalculateRestPointInternal(foot, center);
        Vector3 current = FlattenToPlane(foot.currentPos);
        return Vector3.Distance(current, restPoint);
    }

    private void MoveFootToRest(Foot foot, Vector3 center)
    {
        if (foot == null)
            return;

        BeginFootStep(foot);
        Vector3 restPoint = CalculateRestPointInternal(foot, center);
        foot.desiredPos = restPoint;
        foot.desiredRot = bodyRoot != null ? bodyRoot.rotation : transform.rotation;

        bool alreadyAtRest = Vector3.Distance(FlattenToPlane(foot.currentPos), restPoint) <= RestDistanceTolerance;
        foot.plantState = alreadyAtRest ? FootPlantState.Planted : FootPlantState.Stepping;

        if (!alreadyAtRest)
        {
            lastRestCommandTime = GetCurrentTime();
        }

        ApplyDesiredMarker(foot);
    }

    private void BeginFootStep(Foot foot)
    {
        if (foot == null)
            return;

        if (foot.plantState == FootPlantState.Stepping)
            return;

        foot.plantState = FootPlantState.Stepping;
        foot.stepStartPos = FlattenToPlane(foot.currentPos);
        foot.stepProgress = 0f;
    }

    private FootSide GetSideForFoot(Foot foot)
    {
        if (foot == leftFoot)
            return FootSide.Left;

        if (foot == rightFoot)
            return FootSide.Right;

        return FootSide.Left;
    }

    private void CacheDefaultRestOffset(Foot foot, Vector3 flattenedFootPosition)
    {
        if (foot == null)
            return;

        FootSide side = GetSideForFoot(foot);
        Vector3 center = FlattenToPlane(GetCircleCenter());
        Vector3 offsetWorld = flattenedFootPosition - center;

        if (offsetWorld.sqrMagnitude < 0.0001f)
        {
            foot.hasDefaultRestOffset = false;
            return;
        }

        Quaternion circleRotation = GetCircleRotation();
        Quaternion inverse = Quaternion.Inverse(circleRotation);
        Vector3 localOffset = inverse * offsetWorld;
        localOffset.y = 0f;

        if (localOffset.sqrMagnitude < 0.0001f)
        {
            foot.hasDefaultRestOffset = false;
            return;
        }

        float xSign = side == FootSide.Right ? 1f : -1f;
        if (Mathf.Abs(localOffset.x) < 0.0001f)
        {
            localOffset.x = xSign * Mathf.Max(localOffset.magnitude, 0.0001f);
        }
        else
        {
            localOffset.x = Mathf.Abs(localOffset.x) * xSign;
        }

        foot.defaultRestOffset = localOffset;
        foot.hasDefaultRestOffset = true;
    }

    private Vector3 CalculateRestPointInternal(Foot foot, Vector3 center)
    {
        Vector3 offset = GetRestOffsetForFoot(foot);

        Quaternion rotation = GetCircleRotation();
        Vector3 rotatedOffset = rotation * offset;
        Vector3 point = center + rotatedOffset;
        return FlattenToPlane(point);
    }

    private Vector3 GetRestOffsetForFoot(Foot foot)
    {
        Vector3 offset = Vector3.zero;
        float defaultMagnitude = 0f;

        if (foot != null && foot.hasDefaultRestOffset)
        {
            offset = foot.defaultRestOffset;
            defaultMagnitude = offset.magnitude;
        }
        else
        {
            offset = foot == rightFoot ? Vector3.right : Vector3.left;
            defaultMagnitude = offset.magnitude;
        }

        if (offset.sqrMagnitude < 0.0001f)
        {
            offset = foot == rightFoot ? Vector3.right : Vector3.left;
        }

        offset.y = 0f;
        float radius = GetTargetRestRadius(defaultMagnitude);
        return offset.normalized * radius;
    }

    private float GetTargetRestRadius(float defaultMagnitude)
    {
        float configured = Mathf.Max(restRadius, 0f);
        if (configured > 0f)
        {
            return configured;
        }

        if (defaultMagnitude > 0.0001f)
        {
            return defaultMagnitude;
        }

        return Mathf.Max(minCircleRadius, 0.0001f);
    }

    private void EnsureInitialized(Foot foot)
    {
        if (foot == null || foot.initialized)
            return;

        InitializeFoot(foot);
    }

    private bool IsFootPlanted(Foot foot)
    {
        return foot != null && foot.plantState == FootPlantState.Planted;
    }

    private bool IsFootStepping(Foot foot)
    {
        return foot != null && foot.plantState == FootPlantState.Stepping;
    }

    private void EnsureHipHeightCached()
    {
        if (hipHeightCaptured)
            return;

        CacheInitialHipHeight();
    }

    private float GetDominantStepPhase()
    {
        float leftPhase = GetStepPhase(leftFoot);
        float rightPhase = GetStepPhase(rightFoot);
        return Mathf.Max(leftPhase, rightPhase);
    }

    private float GetStepPhase(Foot foot)
    {
        if (foot == null || foot.plantState != FootPlantState.Stepping)
            return 0f;

        return Mathf.Clamp01(foot.stepProgress);
    }

    private void CalculateDesiredTarget(Foot foot, Vector3 center)
    {
        if (foot == null)
            return;

        Vector3 restPoint = CalculateRestPointInternal(foot, center);
        Vector3 travelForward = GetTravelDirectionOnPlane();
        if (travelForward.sqrMagnitude < 0.0001f)
        {
            travelForward = GetForwardOnPlane();
        }

        if (travelForward.sqrMagnitude < 0.0001f)
        {
            travelForward = Vector3.forward;
        }

        float radius = Mathf.Clamp(circleRadius, minCircleRadius, maxCircleRadius);
        Vector3 targetPos = ProjectFromRestToCircle(restPoint, center, travelForward, radius);
        foot.desiredPos = FlattenToPlane(targetPos);
        foot.desiredRot = Quaternion.LookRotation(travelForward, Vector3.up);

        ApplyDesiredMarker(foot);
    }

    private Vector3 ProjectFromRestToCircle(Vector3 restPoint, Vector3 center, Vector3 direction, float radius)
    {
        Vector3 normalizedDirection = FlattenVector(direction);
        if (normalizedDirection.sqrMagnitude < 0.0001f)
        {
            normalizedDirection = Vector3.forward;
        }
        normalizedDirection.Normalize();

        Vector3 origin = FlattenToPlane(restPoint);
        center = FlattenToPlane(center);
        Vector3 offsetFromCenter = origin - center;

        float b = 2f * Vector3.Dot(offsetFromCenter, normalizedDirection);
        float c = offsetFromCenter.sqrMagnitude - radius * radius;
        float discriminant = (b * b) - 4f * c; // a = 1 because direction is normalized

        if (discriminant >= 0f)
        {
            float sqrt = Mathf.Sqrt(discriminant);
            float t1 = (-b - sqrt) * 0.5f;
            float t2 = (-b + sqrt) * 0.5f;

            float chosenT = float.NaN;
            if (t1 >= 0f && t2 >= 0f)
            {
                chosenT = Mathf.Min(t1, t2);
            }
            else if (t1 >= 0f)
            {
                chosenT = t1;
            }
            else if (t2 >= 0f)
            {
                chosenT = t2;
            }
            else
            {
                chosenT = Mathf.Max(t1, t2);
            }

            if (!float.IsNaN(chosenT))
            {
                return origin + normalizedDirection * chosenT;
            }
        }

        return center + normalizedDirection * radius;
    }

    private void ProgressFootTowardsDesired(Foot foot)
    {
        if (foot == null)
            return;

        Vector3 desired = FlattenToPlane(foot.desiredPos);
        Vector3 start = FlattenToPlane(foot.stepStartPos);
        Vector3 current = FlattenToPlane(foot.currentPos);

        float stepSpeedValue = Mathf.Max(currentStepSpeed, 0f);
        if (stepSpeedValue <= 0f)
        {
            current = desired;
            foot.stepProgress = 1f;
        }
        else if (foot.plantState == FootPlantState.Stepping && current != desired)
        {
            float totalDistance = Mathf.Max(Vector3.Distance(start, desired), 0.0001f);
            float travel = (stepSpeedValue * Time.deltaTime) / totalDistance;
            foot.stepProgress = Mathf.Clamp01(foot.stepProgress + travel);

            current = Vector3.Lerp(start, desired, foot.stepProgress);

            float arcHeight = Mathf.Max(currentStepArcHeight, 0f);
            float verticalOffset = Mathf.Sin(foot.stepProgress * Mathf.PI) * arcHeight;
            current.y = GetGroundPlaneY() + verticalOffset;
        }
        else
        {
            current = desired;
            foot.stepProgress = 1f;
        }

        foot.currentPos = current;

        if (foot.currentTarget != null)
        {
            foot.currentTarget.position = current;
            foot.currentTarget.rotation = foot.desiredRot;
        }

        bool reached = Vector3.Distance(current, desired) <= 0.0001f;
        if (reached)
        {
            foot.plantState = FootPlantState.Planted;
            foot.stepStartPos = desired;
            foot.stepProgress = 1f;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        EnsureInitialized(leftFoot);
        EnsureInitialized(rightFoot);
        UpdateCircleRadius(false);
        UpdateCircleOffset(false);

        Vector3 center = GetOffsetCircleCenter(true);

        DrawCircle(center, circleRadius, new Color(1f, 0.9f, 0.2f, 0.85f), true);
        DrawCircle(center, minCircleRadius, new Color(0.2f, 0.8f, 1f, 0.6f), true);
        DrawCircle(center, maxCircleRadius, new Color(1f, 0.3f, 0.3f, 0.6f), true);
        DrawCircle(center, GetFallbackRadius(), new Color(0.7f, 0.2f, 1f, 0.5f), true);

        DrawFootMarkers(leftFoot, Color.cyan, Color.green, true);
        DrawFootMarkers(rightFoot, new Color(1f, 0.6f, 0f), Color.magenta, true);
    }

    private float GetGroundPlaneY()
    {
        return groundPlaneY;
    }

    private Vector3 FlattenToPlane(Vector3 position, bool useDebugPlane = false)
    {
        position.y = useDebugPlane ? debugPlaneY : GetGroundPlaneY();
        return position;
    }


    private Vector3 GetCircleCenter()
    {
        return bodyRoot != null ? bodyRoot.position : transform.position;
    }

    private Vector3 GetOffsetCircleCenter(bool useDebugPlane = false)
    {
        Vector3 baseCenter = FlattenToPlane(GetCircleCenter(), useDebugPlane);
        return ApplyCircleOffset(baseCenter, useDebugPlane);
    }

    private Quaternion GetCircleRotation()
    {
        Vector3 forward = GetForwardOnPlane();
        return Quaternion.LookRotation(forward, Vector3.up);
    }

    private Quaternion GetTravelRotation()
    {
        Vector3 forward = GetTravelDirectionOnPlane();
        return Quaternion.LookRotation(forward, Vector3.up);
    }

    private Vector3 GetTravelDirectionOnPlane()
    {
        Vector3 travel = FlattenVector(lastMovementDirection);
        if (travel.sqrMagnitude < 0.0001f)
        {
            travel = GetForwardOnPlane();
        }

        if (travel.sqrMagnitude < 0.0001f)
        {
            return Vector3.forward;
        }

        return travel.normalized;
    }

    private Vector3 GetForwardOnPlane()
    {
        Vector3 forward = bodyRoot != null ? bodyRoot.forward : transform.forward;
        Vector3 flattened = FlattenVector(forward);
        if (flattened.sqrMagnitude < 0.0001f)
        {
            return Vector3.forward;
        }

        return flattened.normalized;
    }

    private Vector3 FlattenVector(Vector3 vector)
    {
        vector.y = 0f;
        return vector;
    }

    private Vector3 ApplyCircleOffset(Vector3 baseCenter, bool useDebugPlane = false)
    {
        return FlattenToPlane(baseCenter + currentCircleOffset, useDebugPlane);
    }

    private void DrawCircle(Vector3 center, float radius, Color color, bool useDebugPlane = false)
    {
        const int segments = 32;
        Quaternion rotation = GetCircleRotation();
        Vector3 prev = Vector3.zero;
        bool hasPrev = false;
        Gizmos.color = color;

        for (int i = 0; i <= segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2f;
            Vector3 local = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            Vector3 point = center + (rotation * local) * radius;
            point = FlattenToPlane(point, useDebugPlane);

            if (hasPrev)
            {
                Gizmos.DrawLine(prev, point);
            }

            prev = point;
            hasPrev = true;
        }
    }

    private void DrawFootMarkers(Foot foot, Color currentColor, Color desiredColor, bool useDebugPlane = false)
    {
        if (foot == null)
            return;

        Vector3 current = FlattenToPlane(foot.currentPos, useDebugPlane);
        Vector3 desired = FlattenToPlane(foot.desiredPos, useDebugPlane);

        Gizmos.color = currentColor;
        Gizmos.DrawSphere(current, 0.035f);

        Gizmos.color = desiredColor;
        Gizmos.DrawSphere(desired, 0.035f);

        Gizmos.color = Color.white;
        Gizmos.DrawLine(current, desired);
    }
#endif
}
