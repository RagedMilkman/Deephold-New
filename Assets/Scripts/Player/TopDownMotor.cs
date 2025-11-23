using System;
using RootMotion.FinalIK;
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
    [SerializeField] float gravity = -25f;

    [Header("Mouse Aim")]
    [SerializeField] float aimRayMaxDistance = 1000f;
    [SerializeField] bool useGroundLayerMask = false;
    [SerializeField] LayerMask groundMask = ~0;
    [SerializeField] LayerMask floorMask = 0;
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
    [SerializeField] AimIK aimIK;
    [SerializeField] float headLookFallbackDistance = 5f;
    [SerializeField, Range(0f, 1f)] float headLookWeight = 1f;

    Vector3 moveVel;
    float verticalVelocity;
    float initialRootYaw;
    Stance currentStance;
    MovementType currentMovementType = MovementType.Standing;

    void Reset()
    {
        controller = GetComponent<CharacterController>();
        if (!aimIK) aimIK = GetComponentInChildren<AimIK>();
        if (!rotateTarget) rotateTarget = body ? body : transform;
        currentMovementType = MovementType.Standing;
    }

    void Awake()
    {
        if (!controller) controller = GetComponent<CharacterController>();
        if (!aimIK) aimIK = GetComponentInChildren<AimIK>();
        if (!rotateTarget) rotateTarget = body ? body : transform;
        initialRootYaw = transform.eulerAngles.y;
        currentStance = defaultStance;
        currentMovementType = MovementType.Standing;
    }

    void OnValidate()
    {
        sidewaysSpeedMultiplier = Mathf.Clamp01(sidewaysSpeedMultiplier);
        backwardSpeedMultiplier = Mathf.Clamp(backwardSpeedMultiplier, 0f, sidewaysSpeedMultiplier);
        activeStanceSpeedMultiplier = Mathf.Clamp01(activeStanceSpeedMultiplier);

        if (!Application.isPlaying)
        {
            if (!controller) controller = GetComponent<CharacterController>();
            if (!aimIK) aimIK = GetComponentInChildren<AimIK>();
            if (!rotateTarget) rotateTarget = body ? body : transform;
        }
    }

    // ----- Owner-side hooks (called by Interaction) -----

    public void SetCamera(Camera ownerCam)
    {
        if (!cam) cam = ownerCam;
    }

    public void SetAimSolver(AimIK solver)
    {
        aimIK = solver;
        UpdateAimIK(null);
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

        if (controller)
        {
            if (controller.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f; // small downward force to keep grounding
            }

            verticalVelocity += gravity * dt;

            Vector3 combinedVelocity = new Vector3(moveVel.x, verticalVelocity, moveVel.z);
            controller.Move(combinedVelocity * dt);
        }

        if (lockRootYaw) LockRootYaw();

        UpdateMovementType(DetermineMovementType(input, wantsSprint));
    }

    public Vector3 CursorTarget { get; private set; }
    public Vector3 PlayerTarget { get; private set; }
    public bool HasCursorTarget { get; private set; }

    public bool TryGetAimTargets(Vector2 screenPosition, out Vector3 cursorTarget, out Vector3 playerTarget)
    {
        cursorTarget = default;
        playerTarget = default;

        HasCursorTarget = false;

        if (!cam) return false;

        bool floorHit = false;

        bool hasFloorMask = floorMask.value != 0;

        Ray ray = cam.ScreenPointToRay(screenPosition);
        if (useGroundLayerMask)
        {
            var hits = Physics.RaycastAll(ray, aimRayMaxDistance, groundMask, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0)
            {
                return false;
            }

            RaycastHit? selectedHit = null;
            foreach (var hit in hits)
            {
                var hitTransform = hit.collider ? hit.collider.transform : null;
                if (!hitTransform)
                {
                    continue;
                }

                if (hitTransform == transform || hitTransform.IsChildOf(transform))
                {
                    continue;
                }

                if (!selectedHit.HasValue || hit.distance < selectedHit.Value.distance)
                {
                    selectedHit = hit;
                }
            }

            if (!selectedHit.HasValue)
            {
                return false;
            }

            var validHit = selectedHit.Value;
            cursorTarget = validHit.point;
            if (hasFloorMask && IsLayerInMask(validHit.collider.gameObject.layer, floorMask))
            {
                floorHit = true;
            }
        }
        else
        {
            var plane = new Plane(Vector3.up, Vector3.zero); // y=0
            if (plane.Raycast(ray, out float enter) && enter > 0f)
            {
                cursorTarget = ray.GetPoint(enter);
                floorHit = true;
            }
            else
            {
                return false;
            }
        }

        playerTarget = cursorTarget;
        if (floorHit)
        {
            playerTarget.y += 1.5f;
        }

        CursorTarget = cursorTarget;
        PlayerTarget = playerTarget;
        HasCursorTarget = true;

        Debug.DrawLine(cursorTarget, playerTarget, Color.wheat);

        return true;
    }

    static bool IsLayerInMask(int layer, LayerMask mask)
        => (mask.value & (1 << layer)) != 0;

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
        if (rotationMode == RotationMode.RotateBody || aimIK == null)
        {
            ApplyYawTo(rotateTarget, yawDeg);
            UpdateAimIK(aimPoint);
            return;
        }

        Vector3 origin = rotateTarget ? rotateTarget.position : transform.position;
        Vector3 target = aimPoint ?? origin + Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward * headLookFallbackDistance;

        UpdateAimIK(target);
    }

    public static void ApplyYawTo(Transform t, float yawDeg)
    {
        if (t) t.rotation = Quaternion.Euler(0f, yawDeg, 0f);
    }

    public void ApplyReplicatedYaw(float yawDeg)
        => ApplyYaw(yawDeg);

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
    }

    public Stance CurrentStance => currentStance;

    public MovementType CurrentMovementType => currentMovementType;

    public void SetStance(Stance stance)
    {
        if (currentStance == stance) return;

        currentStance = stance;
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
    }

    void UpdateAimIK(Vector3? target)
    {
        if (!aimIK)
            return;

        Vector3 origin = rotateTarget ? rotateTarget.position : transform.position;
        Vector3 lookTarget = target ?? origin + (rotateTarget ? rotateTarget.forward : transform.forward) * headLookFallbackDistance;

        aimIK.solver.IKPosition = lookTarget;
        aimIK.solver.IKPositionWeight = headLookWeight;
    }
}
