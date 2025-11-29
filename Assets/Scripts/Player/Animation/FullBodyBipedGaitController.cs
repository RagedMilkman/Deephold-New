using RootMotion.FinalIK;
using UnityEngine;

/// <summary>
/// A smoother, IK-driven biped gait controller that positions foot targets for
/// FinalIK's <see cref="FullBodyBipedIK"/>. It computes desired step points
/// relative to the body root, blends them over time, and raises feet along a
/// small arc to avoid the jumpy/flicky behaviour of the legacy controller.
///
/// Attach this alongside the existing BipedGaitController to evaluate which
/// implementation feels better; both components are independent.
/// </summary>
[DefaultExecutionOrder(100)]
public class FullBodyBipedGaitController : MonoBehaviour
{
    [System.Serializable]
    private class FootState
    {
        public Transform target;
        public FootSide side;

        [HideInInspector] public bool initialized;
        [HideInInspector] public bool stepping;
        [HideInInspector] public float stepTime;
        [HideInInspector] public float stepDuration;
        [HideInInspector] public Vector3 startPos;
    }

    private enum FootSide
    {
        Left,
        Right
    }

    [Header("Rig")]
    [Tooltip("Full Body Biped IK solver the controller should drive.")]
    public FullBodyBipedIK solver;

    [Tooltip("Transform used as the reference for velocity and orientation.")]
    public Transform bodyRoot;

    [Header("Targets")]
    public Transform leftFootTarget;
    public Transform rightFootTarget;

    [Header("Step Settings")]
    [Tooltip("Minimum time a step takes (seconds).")]
    public float minStepDuration = 0.25f;

    [Tooltip("Maximum time a step takes (seconds).")]
    public float maxStepDuration = 0.5f;

    [Tooltip("Minimum forward distance a step may cover.")]
    public float minStepLength = 0.2f;

    [Tooltip("Maximum forward distance a step may cover.")]
    public float maxStepLength = 0.6f;

    [Tooltip("Time after completing a step before another can begin (seconds).")]
    public float stepCooldown = 0.08f;

    [Tooltip("Lateral spacing from the body's center.")]
    public float footSpacing = 0.18f;

    [Tooltip("Height of the parabolic arc used when stepping.")]
    public float stepHeight = 0.06f;

    [Tooltip("How far a planted foot can drift from its desired point before forcing a step.")]
    public float stepThreshold = 0.1f;

    [Tooltip("Normalized progress the opposite foot must reach before this foot can start a new step (0-1).")]
    [Range(0f, 1f)]
    public float minOppositeStepLead = 0.35f;

    [Header("Ground")]
    [Tooltip("Offset added to the body root's Y position to define the ground plane.")]
    public float groundOffset = 0f;

    [Header("Anchoring")]
    [Tooltip("Maximum planar distance a desired foot anchor can drift from the body before being clamped closer.")]
    public float maxAnchorOffset = 0.5f;

    [Header("Smoothing")]
    [Tooltip("Time (seconds) used to smooth desired point updates.")]
    public float positionSmoothTime = 0.12f;

    [Tooltip("Multiplier applied to body velocity when predicting step locations.")]
    public float velocityPrediction = 0.5f;

    [Header("Orientation")]
    [Tooltip("How quickly foot targets rotate toward the movement heading (degrees per second).")]
    public float footRotationSpeed = 540f;

    private FootState leftFoot = new FootState { side = FootSide.Left };
    private FootState rightFoot = new FootState { side = FootSide.Right };

    private Vector3 velocity;
    private Vector3 lastBodyPos;
    private Vector3 desiredLeft;
    private Vector3 desiredRight;
    private Vector3 desiredLeftVel;
    private Vector3 desiredRightVel;
    private float leftCooldown;
    private float rightCooldown;

    private void Reset()
    {
        solver = GetComponentInChildren<FullBodyBipedIK>();
        bodyRoot = transform;
    }

    private void Awake()
    {
        lastBodyPos = GetBodyPosition();
        leftFoot.target = leftFootTarget;
        rightFoot.target = rightFootTarget;
        AutoAssignTargetsFromSolver();
        InitializeFoot(leftFoot, -1f);
        InitializeFoot(rightFoot, 1f);

        desiredLeft = leftFoot.target != null ? leftFoot.target.position : GetBodyPosition();
        desiredRight = rightFoot.target != null ? rightFoot.target.position : GetBodyPosition();
    }

    private void Update()
    {
        AutoAssignTargetsFromSolver();
        UpdateVelocity();
        UpdateDesiredAnchors();
        UpdateCooldowns();
        UpdateFoot(leftFoot, rightFoot, desiredLeft, ref desiredLeftVel);
        UpdateFoot(rightFoot, leftFoot, desiredRight, ref desiredRightVel);
    }

    private void AutoAssignTargetsFromSolver()
    {
        if (solver == null || solver.solver == null)
            return;

        if (leftFoot.target == null)
            leftFoot.target = solver.solver.leftFootEffector.target;

        if (rightFoot.target == null)
            rightFoot.target = solver.solver.rightFootEffector.target;
    }

    private void UpdateVelocity()
    {
        Vector3 current = GetBodyPosition();
        Vector3 delta = current - lastBodyPos;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        velocity = delta / dt;
        lastBodyPos = current;
    }

    private void UpdateCooldowns()
    {
        leftCooldown = Mathf.Max(0f, leftCooldown - Time.deltaTime);
        rightCooldown = Mathf.Max(0f, rightCooldown - Time.deltaTime);
    }

    private void UpdateDesiredAnchors()
    {
        Vector3 forward = GetForwardOnPlane();
        Vector3 planarVelocity = new Vector3(velocity.x, 0f, velocity.z);
        Vector3 direction = planarVelocity.sqrMagnitude > 0.0001f ? planarVelocity.normalized : forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        float speed = planarVelocity.magnitude;
        float t = Mathf.InverseLerp(0f, 4f, speed);

        float length = Mathf.Lerp(minStepLength, maxStepLength, t);
        float duration = Mathf.Lerp(maxStepDuration, minStepDuration, t);

        Vector3 predictedMove = direction * (length * velocityPrediction);
        Vector3 desiredLeftTarget = ClampAnchor(GetBodyPosition() + predictedMove - right * footSpacing);
        Vector3 desiredRightTarget = ClampAnchor(GetBodyPosition() + predictedMove + right * footSpacing);

        desiredLeft = SmoothAnchor(desiredLeft, desiredLeftTarget, ref desiredLeftVel);
        desiredRight = SmoothAnchor(desiredRight, desiredRightTarget, ref desiredRightVel);

        leftFoot.stepDuration = duration;
        rightFoot.stepDuration = duration;
    }

    private Vector3 SmoothAnchor(Vector3 current, Vector3 target, ref Vector3 velocityRef)
    {
        float smooth = Mathf.Max(positionSmoothTime, 0.0001f);
        return Vector3.SmoothDamp(current, target, ref velocityRef, smooth);
    }

    private void UpdateFoot(FootState foot, FootState otherFoot, Vector3 anchor, ref Vector3 vel)
    {
        if (foot.target == null)
            return;

        if (!foot.initialized)
        {
            InitializeFoot(foot, foot.side == FootSide.Left ? -1f : 1f);
        }

        Vector3 targetOnPlane = ProjectToGround(anchor);
        Vector3 current = foot.target.position;

        float planarSpeed = new Vector3(velocity.x, 0f, velocity.z).magnitude;
        float dynamicThreshold = Mathf.Lerp(stepThreshold, stepThreshold * 0.35f, Mathf.InverseLerp(0f, 4f, planarSpeed));
        bool coolingDown = GetCooldownForFoot(foot) > 0f;

        bool otherFootBlocking = otherFoot != null && otherFoot.stepping && otherFoot.stepTime < minOppositeStepLead;

        if (!foot.stepping && !coolingDown && !otherFootBlocking && Vector3.Distance(ProjectToGround(current), targetOnPlane) > dynamicThreshold)
        {
            BeginStep(foot, targetOnPlane);
        }

        if (foot.stepping)
        {
            float dt = Time.deltaTime;
            foot.stepTime = Mathf.Clamp01(foot.stepTime + dt / Mathf.Max(foot.stepDuration, 0.0001f));
            float eased = EaseInOut(foot.stepTime);
            float arc = Mathf.Sin(eased * Mathf.PI) * stepHeight;
            Vector3 interpolated = Vector3.Lerp(foot.startPos, targetOnPlane, eased);
            interpolated.y += arc;
            SetFootPosition(foot, interpolated);
            SetFootRotation(foot, targetOnPlane - foot.startPos);

            if (foot.stepTime >= 1f - 0.0001f)
            {
                foot.stepping = false;
                foot.startPos = targetOnPlane;
                ApplyCooldown(foot);
            }
        }
        else
        {
            Vector3 smoothed = SmoothAnchor(current, targetOnPlane, ref vel);
            SetFootPosition(foot, smoothed);
            SetFootRotation(foot, targetOnPlane - smoothed);
        }
    }

    private void BeginStep(FootState foot, Vector3 target)
    {
        foot.stepping = true;
        foot.stepTime = 0f;
        foot.startPos = foot.target.position;
        foot.startPos = ProjectToGround(foot.startPos);
        SetFootRotation(foot, target - foot.startPos);
    }

    private void ApplyCooldown(FootState foot)
    {
        if (foot.side == FootSide.Left)
            leftCooldown = stepCooldown;
        else
            rightCooldown = stepCooldown;
    }

    private void InitializeFoot(FootState foot, float lateralSign)
    {
        Vector3 body = GetBodyPosition();
        Vector3 forward = GetForwardOnPlane();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 start = body + right * (footSpacing * lateralSign);
        start.y = body.y;

        foot.initialized = true;
        foot.stepping = false;
        foot.startPos = start;
        SetFootPosition(foot, start);
    }

    private void SetFootPosition(FootState foot, Vector3 pos)
    {
        if (foot.target != null)
        {
            foot.target.position = pos;
        }
    }

    private void SetFootRotation(FootState foot, Vector3 direction)
    {
        if (foot.target == null)
            return;

        Vector3 planar = direction;
        planar.y = 0f;

        if (planar.sqrMagnitude < 0.0001f)
            planar = GetForwardOnPlane();

        Quaternion desired = Quaternion.LookRotation(planar.normalized, Vector3.up);
        foot.target.rotation = Quaternion.RotateTowards(foot.target.rotation, desired, footRotationSpeed * Time.deltaTime);
    }

    private float GetCooldownForFoot(FootState foot)
    {
        return foot.side == FootSide.Left ? leftCooldown : rightCooldown;
    }

    private float EaseInOut(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t); // smootherstep
    }

    private Vector3 GetBodyPosition()
    {
        return bodyRoot != null ? bodyRoot.position : transform.position;
    }

    private Vector3 GetForwardOnPlane()
    {
        Vector3 forward = bodyRoot != null ? bodyRoot.forward : transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        return forward.normalized;
    }

    private Vector3 ProjectToGround(Vector3 position)
    {
        position.y = GetGroundHeight();
        return position;
    }

    private Vector3 ClampAnchor(Vector3 target)
    {
        Vector3 body = GetBodyPosition();
        Vector3 delta = target - body;
        delta.y = 0f;

        float limit = Mathf.Max(maxAnchorOffset, 0.001f);
        if (delta.magnitude > limit)
        {
            delta = delta.normalized * limit;
            target = body + delta;
        }

        return target;
    }

    private float GetGroundHeight()
    {
        float baseY = bodyRoot != null ? bodyRoot.position.y : transform.position.y;
        return baseY + groundOffset;
    }
}
