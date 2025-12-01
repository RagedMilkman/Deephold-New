using System;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class TopDownMotor : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Camera cam;                      // local-only (owner view)
    [SerializeField] Transform rotateTarget;          // child that rotates (RotatingBody)
    [SerializeField] Transform body;                  // facing reference (Body)
    [SerializeField] CharacterController controller;  // on root Player

    [Header("Movement")]
    [SerializeField] float moveSpeed = 4f;
    [SerializeField] float acceleration = 12f;
    [SerializeField, Range(0f, 1f)] float sidewaysSpeedMultiplier = 0.75f;
    [SerializeField, Range(0f, 1f)] float backwardSpeedMultiplier = 0.6f;
    [SerializeField, Range(0f, 1f)] float activeStanceSpeedMultiplier = 0.65f;

    [Header("Aiming")]
    [SerializeField] float minAimDistance = 0.05f;

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
    }

    [Header("Stance")]
    [SerializeField] Stance defaultStance = Stance.Passive;

    public event Action<Stance> StanceChanged;

    public enum RotationMode
    {
        RotateBody,
        RotateHead
    }

    [Header("Root Rotation")]
    [SerializeField] bool lockRootYaw = true;   // WASD never rotates player root/camera
    [SerializeField] RotationMode rotationMode = RotationMode.RotateBody;
    [SerializeField] HumanoidRigAnimator rigAnimator;
    [SerializeField] float headLookFallbackDistance = 5f;

    Vector3 moveVel;
    float initialRootYaw;
    Stance currentStance;
    MovementType currentMovementType = MovementType.Standing;

    void Reset()
    {
        controller = GetComponent<CharacterController>();
        if (!rigAnimator) rigAnimator = GetComponentInChildren<HumanoidRigAnimator>();
        if (!rotateTarget) rotateTarget = body ? body : transform;
        UpdateRigYawTarget();
        currentMovementType = MovementType.Standing;
        UpdateRigAnimatorState();
    }

    void Awake()
    {
        if (!controller) controller = GetComponent<CharacterController>();
        if (!rigAnimator) rigAnimator = GetComponentInChildren<HumanoidRigAnimator>();
        if (!rotateTarget) rotateTarget = body ? body : transform;
        UpdateRigYawTarget();
        initialRootYaw = transform.eulerAngles.y;
        currentStance = defaultStance;
        currentMovementType = MovementType.Standing;
        UpdateRigAnimatorState();
    }

    void OnValidate()
    {
        sidewaysSpeedMultiplier = Mathf.Clamp01(sidewaysSpeedMultiplier);
        backwardSpeedMultiplier = Mathf.Clamp(backwardSpeedMultiplier, 0f, sidewaysSpeedMultiplier);
        activeStanceSpeedMultiplier = Mathf.Clamp01(activeStanceSpeedMultiplier);

        if (!Application.isPlaying)
        {
            if (!controller) controller = GetComponent<CharacterController>();
            if (!rigAnimator) rigAnimator = GetComponentInChildren<HumanoidRigAnimator>();
            if (!rotateTarget) rotateTarget = body ? body : transform;
            UpdateRigYawTarget();
        }
    }

    // ----- Owner-side hooks (called by Interaction) -----

    public void SetCamera(Camera ownerCam)
    {
        if (!cam) cam = ownerCam;
    }

    public void SetRigAnimator(HumanoidRigAnimator animator)
    {
        rigAnimator = animator;
        UpdateRigYawTarget();
        UpdateRigAnimatorState();
    }

    public void TickMove(Vector2 input, float dt)
        => TickMove(input, false, dt);

    public void TickMove(Vector2 input, bool wantsSprint, float dt)
    {
        Vector3 targetVel;

        Vector3 referenceForward = Vector3.forward;

        if (cam)
        {
            Vector3 camForward = cam.transform.forward;
            camForward.y = 0f;

            if (camForward.sqrMagnitude < 0.0001f)
            {
                camForward = Vector3.forward;
            }
            else
            {
                camForward.Normalize();
            }

            Vector3 camRight = Vector3.Cross(Vector3.up, camForward);
            targetVel = (camRight * input.x) + (camForward * input.y);
            referenceForward = camForward;
        }
        else
        {
            targetVel = new Vector3(input.x, 0f, input.y);
        }

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

            float stanceSpeedMultiplier = currentStance == Stance.Active ? activeStanceSpeedMultiplier : 1f;
            float targetSpeed = moveSpeed * inputMagnitude * speedMultiplier * stanceSpeedMultiplier;
            targetVel = desiredDir * targetSpeed;
        }
        else
        {
            targetVel = Vector3.zero;
        }

        moveVel = Vector3.Lerp(moveVel, targetVel, acceleration * dt);

        if (controller) controller.Move(moveVel * dt);

        if (lockRootYaw) LockRootYaw();

        UpdateMovementType(DetermineMovementType(input, wantsSprint));
    }

    public Vector3 CursorTarget { get; private set; }
    public Vector3 PlayerTarget { get; private set; }
    public bool HasCursorTarget { get; private set; }

    public void SetAimTargets(Vector3 cursorTarget, Vector3 playerTarget)
    {
        CursorTarget = cursorTarget;
        PlayerTarget = playerTarget;
        HasCursorTarget = true;
    }

    public void ClearAimTargets()
    {
        HasCursorTarget = false;
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

    public void ApplyYaw(float yawDeg, Vector3? aimPoint = null)
    {
        if (rotationMode != RotationMode.RotateHead || rigAnimator == null)
        {
            ApplyYawTo(rotateTarget, yawDeg);
        }

        if (rotationMode == RotationMode.RotateHead && rigAnimator != null)
        {
            Vector3 origin = rotateTarget ? rotateTarget.position : transform.position;
            Vector3 target = aimPoint ?? origin + Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward * headLookFallbackDistance;

            UpdateHeadLook(target);
            return;
        }

        UpdateHeadLook(aimPoint);
    }

    public static void ApplyYawTo(Transform t, float yawDeg)
    {
        if (t) t.rotation = Quaternion.Euler(0f, yawDeg, 0f);
    }

    public void ApplyReplicatedYaw(float yawDeg)
        => ApplyYaw(yawDeg);

    public void ApplyReplicatedPosition(Vector3 position)
    {
        transform.position = position;
    }

    public void LockRootYaw()
    {
        var e = transform.eulerAngles;
        transform.rotation = Quaternion.Euler(0f, initialRootYaw, 0f);
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

    MovementType DetermineMovementType(Vector2 input, bool wantsSprint)
    {
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
