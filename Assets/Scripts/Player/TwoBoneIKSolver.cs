using UnityEngine;

/// <summary>
/// Analytic 2-bone IK solver for a single limb:
/// shoulder -> elbow -> wrist
///
/// - Solves positions for a, b, d triangle using cosine rule.
/// - Uses a pole to choose bend direction.
/// - Converts solved positions into rotations by aligning current bone
///   directions to new directions.
/// - Optionally applies LimbIKBoneConstraint after solving.
/// - Designed to be "pure": just the 2-bone chain; hand / grip logic can be a separate layer.
///
/// Usage:
/// - shoulder: upper arm root joint
/// - elbow:    elbow joint
/// - wrist:    joint at base of hand
/// - target:   where the wrist should go
/// - pole:     where the elbow should bend toward
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(350)]
public class TwoBoneIKSolver : MonoBehaviour
{
    public enum ConstraintMode
    {
        Ignore,      // Pure analytic IK, no constraints used
        ApplyAfter,  // Solve, then clamp with constraints
        Inline       // For now behaves like ApplyAfter; can be refined later
    }

    public enum LimbDesignation
    {
        LeftLeg,
        RightLeg,
        LeftArm,
        RightArm,
        Other
    }

    [Header("Joints")]
    public Transform shoulder;
    public Transform elbow;
    public Transform wrist;

    [Header("Targets")]
    public Transform target;
    public Transform pole;

    [Header("Limb Identification")]
    [Tooltip("Used for editor-facing identification of this solver's limb.")]
    public LimbDesignation limbDesignation = LimbDesignation.Other;

    [Header("Constraints")]
    public ConstraintMode constraintMode = ConstraintMode.ApplyAfter;

    [Header("Solve Settings")]
    [Range(0f, 1f)]
    public float ikWeight = 1f;          // blend FK/IK at solver level

    public bool allowStretch = false;
    public float maxStretchRatio = 1.1f; // only used if allowStretch

    [Header("Constraint Solve")]
    [Min(1)]
    public int constraintSolveIterations = 1;
    [Min(0f)]
    public float constraintSolveTolerance = 0.001f;
    [Min(0)]
    public int constraintSolveResetIterations = 0;

    [Header("Execution")]
    public bool autoSolve = true;
    public bool solveInEditMode = true;
    public bool debug = false;

    private LimbIKBoneConstraint shoulderConstraint;
    private LimbIKBoneConstraint elbowConstraint;
    private LimbIKBoneConstraint wristConstraint;

    private Quaternion defaultShoulderLocalRotation;
    private Quaternion defaultElbowLocalRotation;
    private Quaternion defaultWristLocalRotation;
    private bool defaultPoseCached;
    private int accumulatedFailedConstraintIterations;
    private bool pendingInitialSolve;

    private void Awake()
    {
        InitializeSolver();
    }

    private void OnEnable()
    {
        InitializeSolver();
        pendingInitialSolve = true;
    }

    private void OnValidate()
    {
        InitializeSolver();

        if (maxStretchRatio < 1f) maxStretchRatio = 1f;
        if (constraintSolveIterations < 1) constraintSolveIterations = 1;
        if (constraintSolveTolerance < 0f) constraintSolveTolerance = 0f;
        if (constraintSolveResetIterations < 0) constraintSolveResetIterations = 0;
    }

    private void InitializeSolver()
    {
        CacheConstraints();
        CacheDefaultPose();
    }

    private void CacheConstraints()
    {
        if (shoulder != null)
            shoulderConstraint = shoulder.GetComponent<LimbIKBoneConstraint>();
        if (elbow != null)
            elbowConstraint = elbow.GetComponent<LimbIKBoneConstraint>();
        if (wrist != null)
            wristConstraint = wrist.GetComponent<LimbIKBoneConstraint>();
    }

    private void CacheDefaultPose()
    {
        if (shoulder == null || elbow == null || wrist == null)
        {
            defaultPoseCached = false;
            return;
        }

        defaultShoulderLocalRotation = shoulder.localRotation;
        defaultElbowLocalRotation = elbow.localRotation;
        defaultWristLocalRotation = wrist.localRotation;
        defaultPoseCached = true;
    }

    private void ApplyDefaultPose()
    {
        if (!defaultPoseCached)
            CacheDefaultPose();

        if (!defaultPoseCached)
            return;

        shoulder.localRotation = defaultShoulderLocalRotation;
        elbow.localRotation = defaultElbowLocalRotation;
        wrist.localRotation = defaultWristLocalRotation;
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying && !solveInEditMode)
        {
            pendingInitialSolve = false;
            return;
        }

        if (pendingInitialSolve)
        {
            Solve();
            pendingInitialSolve = false;

            if (!autoSolve)
                return;
        }

        if (!autoSolve)
            return;

        Solve();
    }

    /// <summary>
    /// Perform a 2-bone IK solve for shoulder->elbow->wrist.
    /// </summary>
    public void Solve()
    {
        if (ikWeight <= 0f) return;
        if (shoulder == null || elbow == null || wrist == null || target == null)
            return;

        if (!defaultPoseCached)
            CacheDefaultPose();

        bool constraintsActive = constraintMode != ConstraintMode.Ignore;
        int passes = constraintsActive ? Mathf.Max(1, constraintSolveIterations) : 1;
        bool useToleranceBreak = constraintsActive && constraintSolveTolerance > 0f && passes > 1;
        float toleranceSqr = constraintSolveTolerance * constraintSolveTolerance;

        int iterationsUsed;
        bool solved = RunSolvePasses(passes, useToleranceBreak, toleranceSqr, out iterationsUsed);

        if (!constraintsActive || !useToleranceBreak)
        {
            accumulatedFailedConstraintIterations = 0;
            return;
        }

        if (solved)
        {
            accumulatedFailedConstraintIterations = 0;
            return;
        }

        accumulatedFailedConstraintIterations += iterationsUsed;
        if (constraintSolveResetIterations <= 0 || accumulatedFailedConstraintIterations < constraintSolveResetIterations)
            return;

        accumulatedFailedConstraintIterations = 0;
        ApplyDefaultPose();
        RunSolvePasses(passes, useToleranceBreak, toleranceSqr, out _);
    }

    private bool RunSolvePasses(int passes, bool useToleranceBreak, float toleranceSqr, out int iterationsUsed)
    {
        iterationsUsed = 0;
        bool solved = !useToleranceBreak;

        for (int i = 0; i < passes; i++)
        {
            iterationsUsed++;
            SolveInternal();

            if (!useToleranceBreak)
                continue;

            float wristErrorSqr = (wrist.position - target.position).sqrMagnitude;
            if (wristErrorSqr <= toleranceSqr)
                return true;

            solved = false;
        }

        return solved;
    }

    private void SolveInternal()
    {

        Vector3 shoulderPos = shoulder.position;
        Vector3 elbowPos = elbow.position;
        Vector3 wristPos = wrist.position;
        Vector3 targetPos = target.position;

        // Segment lengths
        float upperLen = Vector3.Distance(shoulderPos, elbowPos);
        float lowerLen = Vector3.Distance(elbowPos, wristPos);

        if (upperLen < 1e-6f || lowerLen < 1e-6f)
            return;

        // Direction and distance from shoulder to target
        Vector3 rootToTarget = targetPos - shoulderPos;
        float d = rootToTarget.magnitude;

        if (d < 1e-6f)
            return;

        Vector3 rootDir = rootToTarget / d;

        // Stretch handling & triangle inequality
        float minReach = Mathf.Abs(upperLen - lowerLen) + 1e-5f;
        float maxReach = (upperLen + lowerLen - 1e-5f);

        if (allowStretch)
            maxReach *= maxStretchRatio;

        d = Mathf.Clamp(d, minReach, maxReach);
        Vector3 adjustedTargetPos = shoulderPos + rootDir * d;

        // Compute planar basis using the pole
        Vector3 poleDirWorld = Vector3.up;
        if (pole != null)
            poleDirWorld = pole.position - shoulderPos;

        // Remove component along rootDir to get genuine bend direction
        Vector3 bendDir = Vector3.ProjectOnPlane(poleDirWorld, rootDir);
        if (bendDir.sqrMagnitude < 1e-6f)
        {
            // If pole is degenerate, pick any perpendicular to rootDir
            bendDir = Vector3.Cross(rootDir, Vector3.up);
            if (bendDir.sqrMagnitude < 1e-6f)
                bendDir = Vector3.Cross(rootDir, Vector3.forward);
        }
        bendDir.Normalize();

        // Law of cosines in that plane:
        // Place elbow at distance 'upperLen' from shoulder, so that wrist at 'lowerLen'
        // from elbow can reach the target at distance 'd'.
        // x is along rootDir, y along bendDir.
        float a = upperLen;
        float b = lowerLen;

        float x = (a * a + d * d - b * b) / (2f * d);
        float ySq = a * a - x * x;
        float y = ySq > 0f ? Mathf.Sqrt(ySq) : 0f;

        Vector3 newElbowPos = shoulderPos + rootDir * x + bendDir * y;
        Vector3 newWristPos = shoulderPos + rootDir * d; // same as adjustedTargetPos

        // --- Convert solved positions into rotations ---

        Quaternion shoulderSolvedRot = shoulder.rotation;
        Quaternion elbowSolvedRot = elbow.rotation;
        Quaternion wristSolvedRot = wrist.rotation;

        // Shoulder: aim from shoulder->elbow to shoulder->newElbow
        {
            Vector3 currentDir = elbowPos - shoulderPos;
            Vector3 solvedDir = newElbowPos - shoulderPos;

            if (currentDir.sqrMagnitude > 1e-8f && solvedDir.sqrMagnitude > 1e-8f)
            {
                currentDir.Normalize();
                solvedDir.Normalize();
                Quaternion fromTo = Quaternion.FromToRotation(currentDir, solvedDir);
                shoulderSolvedRot = fromTo * shoulder.rotation;
            }
        }

        // Elbow: aim from elbow->wrist to elbow->newWrist
        {
            Vector3 currentDir = wristPos - elbowPos;
            Vector3 solvedDir = newWristPos - newElbowPos;

            if (currentDir.sqrMagnitude > 1e-8f && solvedDir.sqrMagnitude > 1e-8f)
            {
                currentDir.Normalize();
                solvedDir.Normalize();
                Quaternion fromTo = Quaternion.FromToRotation(currentDir, solvedDir);
                elbowSolvedRot = fromTo * elbow.rotation;
            }
        }

        // Wrist orientation we leave mostly alone here; hand/grip system can adjust
        // but we allow some blending if you want it to look at the target a bit.
        // For now, we keep whatever rotation the wrist had; position is what matters.
        wristSolvedRot = wrist.rotation;

        // --- Apply IK weight ---
        shoulderSolvedRot = Quaternion.Slerp(shoulder.rotation, shoulderSolvedRot, ikWeight);
        elbowSolvedRot = Quaternion.Slerp(elbow.rotation, elbowSolvedRot, ikWeight);
        wristSolvedRot = Quaternion.Slerp(wrist.rotation, wristSolvedRot, ikWeight);

        // --- Apply constraints depending on mode ---
        switch (constraintMode)
        {
            case ConstraintMode.Ignore:
                ApplyRotationsDirect(shoulderSolvedRot, elbowSolvedRot, wristSolvedRot);
                break;

            case ConstraintMode.ApplyAfter:
            case ConstraintMode.Inline: // currently same behaviour; elbow-angle inline could be added later
                ApplyRotationsWithConstraints(shoulderSolvedRot, elbowSolvedRot, wristSolvedRot);
                break;
        }

        // Debug vectors
        if (debug)
        {
            Debug.DrawLine(shoulder.position, elbow.position, Color.cyan);
            Debug.DrawLine(elbow.position, wrist.position, Color.cyan);
            Debug.DrawLine(shoulder.position, target.position, Color.magenta);
        }
    }

    private void ApplyRotationsDirect(Quaternion shoulderWorld, Quaternion elbowWorld, Quaternion wristWorld)
    {
        shoulder.rotation = shoulderWorld;
        elbow.rotation = elbowWorld;
        wrist.rotation = wristWorld;
    }

    private void ApplyRotationsWithConstraints(Quaternion shoulderWorld, Quaternion elbowWorld, Quaternion wristWorld)
    {
        // Shoulder
        Quaternion finalShoulder = shoulderWorld;
        if (shoulderConstraint != null)
        {
            Quaternion parentRot = shoulder.parent ? shoulder.parent.rotation : Quaternion.identity;
            Quaternion currentLoc = shoulder.localRotation;
            finalShoulder = shoulderConstraint.ConstrainRotation(parentRot, shoulderWorld, currentLoc);
        }
        shoulder.rotation = finalShoulder;

        // Elbow (parent is shoulder which we just set)
        Quaternion finalElbow = elbowWorld;
        if (elbowConstraint != null)
        {
            Quaternion parentRot = shoulder.rotation; // already constrained
            Quaternion currentLoc = elbow.localRotation;
            finalElbow = elbowConstraint.ConstrainRotation(parentRot, elbowWorld, currentLoc);
        }
        elbow.rotation = finalElbow;

        // Wrist (parent is elbow)
        Quaternion finalWrist = wristWorld;
        if (wristConstraint != null)
        {
            Quaternion parentRot = elbow.rotation;
            Quaternion currentLoc = wrist.localRotation;
            finalWrist = wristConstraint.ConstrainRotation(parentRot, wristWorld, currentLoc);
        }
        wrist.rotation = finalWrist;
    }
}
