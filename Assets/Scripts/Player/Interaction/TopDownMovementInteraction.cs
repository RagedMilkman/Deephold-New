using UnityEngine;
using UnityEngine.InputSystem;

public class TopDownMovementInteraction : PlayerInteraction
{
    [SerializeField] TopDownMotor motor;
    [SerializeField] YawReplicator yawSync;

    protected override void Awake()
    {
        base.Awake();
        if (!motor) motor = GetComponentInChildren<TopDownMotor>();
        if (!yawSync) yawSync = GetComponentInChildren<YawReplicator>();

        // interaction gating
        requireOwner = true;   // local-only control
        requireAlive = true;   // disabled when dead
        allowOnServer = false;  // no server ticking
    }

    protected override void OnInteractionSpawned(bool asServer)
    {
        if (isOwner && motor)
        {
            // resolve the owner camera once
            var cam = GetComponentInChildren<Camera>(true);
            if (!cam) cam = Camera.main;
            motor.SetCamera(cam);
        }
    }

    protected override void OnActiveUpdate()
    {
        if (motor == null) return;

        // --- WASD world-relative movement ---
        var kb = Keyboard.current;
        Vector2 input = Vector2.zero;
        bool wantsSprint = false;

        if (kb != null)
        {
            input.x = (kb.aKey.isPressed ? -1f : 0f) + (kb.dKey.isPressed ? 1f : 0f);
            input.y = (kb.sKey.isPressed ? -1f : 0f) + (kb.wKey.isPressed ? 1f : 0f);
            wantsSprint = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
        }

        // --- Update stance based on secondary mouse button ---
        var mouse = Mouse.current;
        bool activeStance = mouse != null && mouse.rightButton.isPressed;
        motor.SetActiveStance(activeStance);

        motor.TickMove(input, wantsSprint, Time.deltaTime);

        // --- Mouse-aim sets facing (and replicates yaw) ---
        if (mouse != null &&
            motor.TryGetAimTargets(mouse.position.ReadValue(), out var cursorTarget, out var playerTarget) &&
            motor.TryComputeYawFromPoint(cursorTarget, out var yaw))
        {
            motor.ApplyYaw(yaw, playerTarget);  // local visual
            yawSync?.OwnerSetYaw(yaw);      // replicate to others
        }
    }

    protected override void OnInactiveUpdate()
    {
        // Optional: zero local move-only effects if you keep any
    }
}
