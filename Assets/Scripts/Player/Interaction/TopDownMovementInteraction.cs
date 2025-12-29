using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Owner-only top down movement and aiming controller.
/// Drives local player aiming and sends yaw to other clients via <see cref="YawReplicator"/>.
/// </summary>
public class TopDownMovementInteraction : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private TopDownMotor _motor;
    [SerializeField] private Camera _ownerCamera;

    [Header("Aiming")]
    [SerializeField] private float _aimRayMaxDistance = 1000f;
    [SerializeField] private bool _useGroundLayerMask = false;
    [SerializeField] private LayerMask _groundMask = ~0;
    [SerializeField] private LayerMask _floorMask = 0;

    private void Awake()
    {
        if (!_motor) _motor = GetComponentInChildren<TopDownMotor>();
        if (!_ownerCamera) _ownerCamera = GetComponentInChildren<Camera>(true);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (IsOwner)
        {
            EnablePlayerSystems();
            return;
        }

        DisablePlayerSystems();
    }

    private void EnablePlayerSystems()
    {
        enabled = true;

        if (!_ownerCamera) _ownerCamera = Camera.main;
        // Ensure only the player camera contributes audio when it becomes active.
        if (Camera.main && Camera.main != _ownerCamera)
        {
            var mainListener = Camera.main.GetComponent<AudioListener>();
            if (mainListener) mainListener.enabled = false;
        }
    }

    private void DisablePlayerSystems()
    {
        enabled = false;

        if (_ownerCamera)
        {
            _ownerCamera.enabled = false;
            var listener = _ownerCamera.GetComponent<AudioListener>();
            if (listener) listener.enabled = false;
        }

    }

    private void Update()
    {
        if (!IsOwner || _motor == null)
            return;

        // WASD world-relative movement
        Keyboard kb = Keyboard.current;
        Vector2 input = Vector2.zero;
        bool wantsSprint = false;

        if (kb != null)
        {
            input.x = (kb.aKey.isPressed ? -1f : 0f) + (kb.dKey.isPressed ? 1f : 0f);
            input.y = (kb.sKey.isPressed ? -1f : 0f) + (kb.wKey.isPressed ? 1f : 0f);
            wantsSprint = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
        }

        // Update stance based on secondary mouse button
        Mouse mouse = Mouse.current;
        bool activeStance = mouse != null && mouse.rightButton.isPressed;
        _motor.SetActiveStance(activeStance);

        Vector3 moveInputWorld = new Vector3(input.x, 0f, input.y);
        if (_ownerCamera)
        {
            Vector3 camForward = _ownerCamera.transform.forward;
            camForward.y = 0f;
            if (camForward.sqrMagnitude > 0.0001f)
            {
                camForward.Normalize();
            }
            else
            {
                camForward = Vector3.forward;
            }

            Vector3 camRight = Vector3.Cross(Vector3.up, camForward);
            moveInputWorld = (camRight * input.x) + (camForward * input.y);
        }

        _motor.TickMove(moveInputWorld, wantsSprint, Time.deltaTime);

        // Mouse-aim sets facing (and replicates yaw)
        if (mouse != null &&
            TryGetAimTargets(mouse.position.ReadValue(), out var cursorTarget, out var playerTarget) &&
            _motor.TickAim(cursorTarget, playerTarget))
        {
        }
        else
        {
            _motor.ClearAimTargets();
        }
    }

    private bool TryGetAimTargets(Vector2 screenPosition, out Vector3 cursorTarget, out Vector3 playerTarget)
    {
        cursorTarget = default;
        playerTarget = default;

        if (!_ownerCamera || !_motor) return false;

        bool floorHit = false;

        bool hasFloorMask = _floorMask.value != 0;

        Ray ray = _ownerCamera.ScreenPointToRay(screenPosition);
        if (_useGroundLayerMask)
        {
            var hits = Physics.RaycastAll(ray, _aimRayMaxDistance, _groundMask, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0)
            {
                return false;
            }

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            RaycastHit? selectedHit = null;
            foreach (var hit in hits)
            {
                var hitTransform = hit.collider ? hit.collider.transform : null;
                if (!hitTransform)
                {
                    continue;
                }

                if (hitTransform == _motor.transform || hitTransform.IsChildOf(_motor.transform))
                {
                    continue;
                }

                selectedHit = hit;
                break;
            }

            if (!selectedHit.HasValue)
            {
                return false;
            }

            var validHit = selectedHit.Value;
            cursorTarget = validHit.point;
            if (hasFloorMask)
            {
                var firstHits = Physics.RaycastAll(ray, _aimRayMaxDistance, ~0, QueryTriggerInteraction.Ignore);
                System.Array.Sort(firstHits, (a, b) => a.distance.CompareTo(b.distance));

                RaycastHit? firstNonSelfHit = null;

                foreach (var hit in firstHits)
                {
                    var hitTransform = hit.collider ? hit.collider.transform : null;
                    if (!hitTransform)
                    {
                        continue;
                    }

                    if (hitTransform == _motor.transform || hitTransform.IsChildOf(_motor.transform))
                    {
                        continue;
                    }

                    firstNonSelfHit = hit;
                    break;
                }

                if (firstNonSelfHit.HasValue &&
                    IsLayerInMask(firstNonSelfHit.Value.collider.gameObject.layer, _floorMask))
                {
                    floorHit = true;
                }
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

        Debug.DrawLine(cursorTarget, playerTarget, Color.wheat);

        return true;
    }

    private static bool IsLayerInMask(int layer, LayerMask mask)
        => (mask.value & (1 << layer)) != 0;
}
