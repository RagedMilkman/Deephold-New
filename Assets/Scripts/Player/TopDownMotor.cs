using System;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class TopDownMotor : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Transform rotateTarget;          // child that rotates (RotatingBody)
    [SerializeField] Transform body;                  // facing reference (Body)
    [SerializeField] CharacterController controller;  // on root Player
    [SerializeField] YawReplicator yawReplicator;
    [SerializeField] PositionReplicator positionReplicator;
    [SerializeField] CharacterHealth characterState;

    [Header("Movement")]
    [SerializeField] float moveSpeed = 4f;
    [SerializeField] float acceleration = 12f;
    [SerializeField] float gravity = -30f;
    [SerializeField, Range(0f, 1f)] float sidewaysSpeedMultiplier = 0.75f;
    [SerializeField, Range(0f, 1f)] float backwardSpeedMultiplier = 0.6f;
    [SerializeField, Range(0f, 1f)] float activeStanceSpeedMultiplier = 0.65f;
    [SerializeField, Range(0f, 1f)] float externalSpeedMultiplier = 1f;
    [SerializeField, Min(0f)] float sprintSpeedMultiplier = 1.5f;

    [Header("Aiming")]
    [SerializeField] float minAimDistance = 0.05f;

    [SerializeField] bool DebugForceStop;

    public enum Stance
    {
        Passive,
        Active,
    }

    public enum MovementType
    {
        Standing,
        Moving,
        Sprinting,
        Crouching,
    }

    [Header("Stance")]
    [SerializeField] Stance defaultStance = Stance.Passive;

    public event Action<Stance> StanceChanged;

    [Header("Root Rotation")]
    [SerializeField] HumanoidRigAnimator rigAnimator;
    [SerializeField] float headLookFallbackDistance = 5f;

    [Header("Crouch")]
    [SerializeField, Range(0f, 1f)] float crouchSpeedMultiplier = 0.5f;
    [SerializeField] BipedGaitController gaitController;
    [SerializeField] Transform bRoot;
    [SerializeField] float crouchGroundPlaneOffset = 0.5f;
    [SerializeField] float crouchBRootYOffset = -0.5f;
    [SerializeField, Min(0f)] float crouchTransitionDuration = 0.2f;

    Vector3 moveVel;
    float verticalVelocity;
    Stance currentStance;
    MovementType currentMovementType = MovementType.Standing;
    bool isCrouching;
    float defaultGroundPlaneY;
    Vector3 defaultBRootPosition;
    bool baseCrouchStateCaptured;
    Coroutine crouchRoutine;

    void Reset()
    {
        controller = GetComponent<CharacterController>();
        if (!rigAnimator) rigAnimator = GetComponentInChildren<HumanoidRigAnimator>();
        if (!rotateTarget) rotateTarget = body ? body : transform;
        if (!yawReplicator) yawReplicator = GetComponentInChildren<YawReplicator>();
        if (!positionReplicator) positionReplicator = GetComponentInChildren<PositionReplicator>();
        if (!characterState) characterState = GetComponentInParent<CharacterHealth>();
        if (!gaitController) gaitController = GetComponentInChildren<BipedGaitController>();
        TryAssignBodyRoot();
        UpdateRigYawTarget();
        CaptureDefaultCrouchOffsets();
        currentMovementType = MovementType.Standing;
        UpdateRigAnimatorState();
    }

    void Awake()
    {
        if (!controller) controller = GetComponent<CharacterController>();
        if (!rigAnimator) rigAnimator = GetComponentInChildren<HumanoidRigAnimator>();
        if (!rotateTarget) rotateTarget = body ? body : transform;
        if (!yawReplicator) yawReplicator = GetComponentInChildren<YawReplicator>();
        if (!positionReplicator) positionReplicator = GetComponentInChildren<PositionReplicator>();
        if (!characterState) characterState = GetComponentInParent<CharacterHealth>();
        if (!gaitController) gaitController = GetComponentInChildren<BipedGaitController>();
        TryAssignBodyRoot();
        UpdateRigYawTarget();
        CaptureDefaultCrouchOffsets();
        currentStance = defaultStance;
        currentMovementType = MovementType.Standing;
        UpdateRigAnimatorState();
    }

    void OnValidate()
    {
        sidewaysSpeedMultiplier = Mathf.Clamp01(sidewaysSpeedMultiplier);
        backwardSpeedMultiplier = Mathf.Clamp(backwardSpeedMultiplier, 0f, sidewaysSpeedMultiplier);
        activeStanceSpeedMultiplier = Mathf.Clamp01(activeStanceSpeedMultiplier);
        externalSpeedMultiplier = Mathf.Clamp01(externalSpeedMultiplier);
        sprintSpeedMultiplier = Mathf.Max(0f, sprintSpeedMultiplier);

        if (!Application.isPlaying)
        {
            if (!controller) controller = GetComponent<CharacterController>();
            if (!rigAnimator) rigAnimator = GetComponentInChildren<HumanoidRigAnimator>();
            if (!rotateTarget) rotateTarget = body ? body : transform;
            if (!characterState) characterState = GetComponentInParent<CharacterHealth>();
            if (!gaitController) gaitController = GetComponentInChildren<BipedGaitController>();
            TryAssignBodyRoot();
            UpdateRigYawTarget();
        }
    }

    // ----- Owner-side hooks (called by Interaction) -----

    public void SetRigAnimator(HumanoidRigAnimator animator)
    {
        rigAnimator = animator;
        UpdateRigYawTarget();
        UpdateRigAnimatorState();
    }

    public void TickMove(Vector2 input, float dt)
        => TickMove(new Vector3(input.x, 0f, input.y), false, dt);

    public void TickMove(Vector2 input, bool wantsSprint, float dt, bool replicatePosition = true)
        => TickMove(new Vector3(input.x, 0f, input.y), wantsSprint, dt, replicatePosition);
    
    public void TickMove(Vector3 moveInputWorld, bool wantsSprint, float dt, bool replicatePosition = true)
    {
        if (DebugForceStop)
        {
            moveVel = Vector3.zero;
            verticalVelocity = 0f;              // temporarily remove gravity too
            return;                              // <- NO controller.Move call
        }

        if (characterState && characterState.State == LifeState.Dead)
        {
            moveVel = Vector3.zero;
            verticalVelocity = 0f;
            UpdateMovementType(MovementType.Standing);
            return;
        }

        Vector3 targetVel = new Vector3(moveInputWorld.x, 0f, moveInputWorld.z);
        Vector3 referenceForward = Vector3.forward;

        Transform facingReference = body ? body : rotateTarget;
        if (!facingReference)
        {
            facingReference = transform;
        }

        Vector3 facingForward = facingReference.forward;
        facingForward.y = 0f;

        if (facingForward.sqrMagnitude > 0.0001f)
        {
            referenceForward = facingForward.normalized;
        }

        float inputMagnitude = targetVel.magnitude;
        if (inputMagnitude > 0.0001f)
        {
            Vector3 desiredDir = targetVel / inputMagnitude;
            if (inputMagnitude > 1f) inputMagnitude = 1f;

            float dot = Vector3.Dot(desiredDir, referenceForward);
            float speedMultiplier = dot >= 0f
                ? Mathf.Lerp(sidewaysSpeedMultiplier, 1f, dot)
                : Mathf.Lerp(sidewaysSpeedMultiplier, backwardSpeedMultiplier, -dot);

            speedMultiplier = Mathf.Max(0f, speedMultiplier);


            float stanceSpeedMultiplier = currentStance == Stance.Active ? activeStanceSpeedMultiplier : 1f;
            float crouchMultiplier = isCrouching ? crouchSpeedMultiplier : 1f;
            float sprintMultiplier = wantsSprint && !isCrouching ? sprintSpeedMultiplier : 1f;
            float targetSpeed = moveSpeed * inputMagnitude * speedMultiplier * stanceSpeedMultiplier * crouchMultiplier * sprintMultiplier * externalSpeedMultiplier;
            targetVel = desiredDir * targetSpeed;
        }
        else
        {
            targetVel = Vector3.zero;
        }

        moveVel = Vector3.Lerp(moveVel, targetVel, acceleration * dt);

        if (controller)
        {
            if (controller.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = 0f;
            }

            verticalVelocity += gravity * dt;

            Vector3 motion = new Vector3(moveVel.x, verticalVelocity, moveVel.z);
            controller.Move(motion * dt);
        }

        UpdateMovementType(DetermineMovementType(targetVel, wantsSprint));

        if (replicatePosition)
        {
            positionReplicator?.SubmitPosition(transform.position);
        }
    }

    public Vector3 CursorTarget { get; private set; }
    public Vector3 PlayerTarget { get; private set; }
    public bool PlayerTargetIsFloor { get; private set; }
    public bool HasCursorTarget { get; private set; }
    public Vector3? CurrentHeadLookTarget => rigAnimator ? rigAnimator.CurrentHeadLookTarget : null;
    public Vector3 FacingForward => rotateTarget ? rotateTarget.forward : transform.forward;
    public Vector3 FacingOrigin => rotateTarget ? rotateTarget.position : transform.position;

    public void SetAimTargets(Vector3 cursorTarget, Vector3 playerTarget, bool playerTargetIsFloor)
    {
        CursorTarget = cursorTarget;
        PlayerTarget = playerTarget;
        PlayerTargetIsFloor = playerTargetIsFloor;
        HasCursorTarget = true;
    }

    public void ClearAimTargets()
    {
        HasCursorTarget = false;
        PlayerTargetIsFloor = false;
    }

    public bool TickAim(Vector3 cursorTarget, Vector3 playerTarget, bool playerTargetIsFloor, bool replicateYaw = true)
    {
        if (TryComputeYawFromPoint(cursorTarget, out var yaw))
        {
            SetAimTargets(cursorTarget, playerTarget, playerTargetIsFloor);
            ApplyYaw(yaw, playerTarget, replicateYaw);
            return true;
        }

        ClearAimTargets();
        return false;
    }

    public bool TryComputeYawFromPoint(Vector3 aimPoint, out float yawDegOut)
    {
        yawDegOut = 0f;
        if (!rotateTarget) return false;

        Vector3 dir = aimPoint - rotateTarget.position;
        dir.y = 0f;
        if (dir.sqrMagnitude <= (minAimDistance * minAimDistance)) return false;

        yawDegOut = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        return true;
    }

    public void ApplyYaw(float yawDeg, Vector3? aimPoint = null, bool replicateYaw = true)
    {
        if (characterState && characterState.State == LifeState.Dead)
        {
            return;
        }

        if (rigAnimator != null)
        {
            Vector3 origin = rotateTarget ? rotateTarget.position : transform.position;
            Vector3 target = aimPoint ?? origin + Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward * headLookFallbackDistance;

            UpdateHeadLook(target);
        }
        else
        {
            ApplyYawTo(rotateTarget, yawDeg);
        }

        if (replicateYaw)
        {
            yawReplicator?.SubmitYaw(yawDeg);
        }
    }

    public static void ApplyYawTo(Transform t, float yawDeg)
    {
        if (t) t.rotation = Quaternion.Euler(0f, yawDeg, 0f);
    }

    public void ApplyReplicatedYaw(float yawDeg)
        => ApplyYaw(yawDeg, null, false);

    public void ApplyReplicatedPosition(Vector3 position)
    {
        transform.position = position;
    }

    public void SetBody(Transform bodyTransform)
    {
        body = bodyTransform;
        if (body && (!rotateTarget || rotateTarget == transform))
        {
            rotateTarget = body;
        }
        UpdateRigYawTarget();
    }

    void UpdateRigYawTarget()
    {
        if (rigAnimator)
        {
            rigAnimator.SetCharacterYawTransform(rotateTarget ? rotateTarget : transform.root);
        }
    }

    public Stance CurrentStance => currentStance;

    public MovementType CurrentMovementType => currentMovementType;
    public bool IsCrouching => isCrouching;

    public void SetExternalSpeedMultiplier(float multiplier)
    {
        externalSpeedMultiplier = Mathf.Clamp01(multiplier);
    }

    public void SetStance(Stance stance)
    {
        if (currentStance == stance) return;

        currentStance = stance;
        if (rigAnimator != null)
        {
            rigAnimator.SetStance(currentStance);
        }
        StanceChanged?.Invoke(currentStance);
    }

    public void SetActiveStance(bool active)
        => SetStance(active ? Stance.Active : Stance.Passive);

    public void SetCrouch(bool crouch)
    {
        if (isCrouching == crouch) return;

        isCrouching = crouch;
        StartCrouchRoutine(isCrouching);
        UpdateMovementType(DetermineMovementType(moveVel, false));
    }

    void CaptureDefaultCrouchOffsets()
    {
        if (baseCrouchStateCaptured)
            return;

        bool capturedAny = false;
        if (gaitController)
        {
            defaultGroundPlaneY = gaitController.groundPlaneY;
            capturedAny = true;
        }

        if (bRoot)
        {
            defaultBRootPosition = bRoot.localPosition;
            capturedAny = true;
        }

        baseCrouchStateCaptured = capturedAny;
    }

    void TryAssignBodyRoot()
    {
        if (bRoot)
            return;

        Transform searchRoot = rigAnimator ? rigAnimator.transform : transform;
        Transform namedRoot = FindFirstNamedTransform(searchRoot, "Rig", "B-root", "B-hips");

        bRoot = namedRoot ? namedRoot : gaitController ? gaitController.bodyRoot : null;
    }

    static Transform FindFirstNamedTransform(Transform root, params string[] names)
    {
        if (!root || names == null || names.Length == 0)
            return null;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            for (int j = 0; j < names.Length; j++)
            {
                if (string.Equals(transforms[i].name, names[j], StringComparison.OrdinalIgnoreCase))
                    return transforms[i];
            }
        }

        return null;
    }

    void StartCrouchRoutine(bool crouch)
    {
        if (!baseCrouchStateCaptured)
        {
            CaptureDefaultCrouchOffsets();
        }

        if (crouchRoutine != null)
        {
            StopCoroutine(crouchRoutine);
        }

        crouchRoutine = StartCoroutine(AnimateCrouchOffsets(crouch));
    }

    IEnumerator AnimateCrouchOffsets(bool crouch)
    {
        float duration = crouchTransitionDuration;
        float elapsed = 0f;

        float startGroundPlaneY = gaitController ? gaitController.groundPlaneY : 0f;
        float targetGroundPlaneY = crouch && gaitController
            ? defaultGroundPlaneY + crouchGroundPlaneOffset
            : defaultGroundPlaneY;

        Vector3 startBRootPosition = bRoot ? bRoot.localPosition : Vector3.zero;
        Vector3 targetBRootPosition = bRoot ? defaultBRootPosition : Vector3.zero;
        if (crouch)
        {
            targetBRootPosition.y += crouchBRootYOffset;
        }

        if (duration <= 0f)
        {
            ApplyCrouchTargets(targetGroundPlaneY, targetBRootPosition);
            yield break;
        }

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float blendedGroundPlaneY = Mathf.Lerp(startGroundPlaneY, targetGroundPlaneY, t);
            Vector3 blendedBRootPosition = Vector3.Lerp(startBRootPosition, targetBRootPosition, t);

            ApplyCrouchTargets(blendedGroundPlaneY, blendedBRootPosition);
            yield return null;
        }

        ApplyCrouchTargets(targetGroundPlaneY, targetBRootPosition);
    }

    void ApplyCrouchTargets(float groundPlaneY, Vector3 bRootPosition)
    {
        if (gaitController)
        {
            gaitController.groundPlaneY = groundPlaneY;
        }

        if (bRoot)
        {
            bRoot.localPosition = bRootPosition;
        }
    }

    MovementType DetermineMovementType(Vector3 input, bool wantsSprint)
    {
        if (isCrouching)
            return MovementType.Crouching;

        if (input.sqrMagnitude <= 0.0001f)
            return MovementType.Standing;

        return wantsSprint ? MovementType.Sprinting : MovementType.Moving;
    }

    void UpdateMovementType(MovementType newType)
    {
        if (currentMovementType == newType)
            return;

        currentMovementType = newType;
        if (rigAnimator != null)
        {
            rigAnimator.SetMovementType(newType);
        }
    }

    void UpdateHeadLook(Vector3? target)
    {
        if (!rigAnimator)
            return;

        Vector3 origin = rotateTarget ? rotateTarget.position : transform.position;
        Vector3 lookTarget = target ?? origin + (rotateTarget ? rotateTarget.forward : transform.forward) * headLookFallbackDistance;

        rigAnimator.SetHeadLookTarget(lookTarget);
    }

    void UpdateRigAnimatorState()
    {
        if (rigAnimator == null)
        {
            return;
        }

        rigAnimator.SetStance(currentStance);
        rigAnimator.SetMovementType(currentMovementType);
    }
}
