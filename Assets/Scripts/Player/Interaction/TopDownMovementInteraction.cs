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
    [SerializeField] private YawReplicator _yawReplicator;
    [SerializeField] private PositionReplicator _positionReplicator;
    [SerializeField] private Camera _ownerCamera;

    private void Awake()
    {
        if (!_motor) _motor = GetComponentInChildren<TopDownMotor>();
        if (!_yawReplicator) _yawReplicator = GetComponentInChildren<YawReplicator>();
        if (!_positionReplicator) _positionReplicator = GetComponentInChildren<PositionReplicator>();
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
        if (_motor && _ownerCamera)
            _motor.SetCamera(_ownerCamera);

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

        _motor.TickMove(input, wantsSprint, Time.deltaTime);
        _positionReplicator?.SubmitPosition(_motor.transform.position);

        // Mouse-aim sets facing (and replicates yaw)
        if (mouse != null &&
            _motor.TryGetAimTargets(mouse.position.ReadValue(), out var cursorTarget, out var playerTarget) &&
            _motor.TryComputeYawFromPoint(cursorTarget, out var yaw))
        {
            _motor.ApplyYaw(yaw, playerTarget);  // local visual
            _yawReplicator?.SubmitYaw(yaw);      // replicate to others
        }
    }
}
