using UnityEngine;

/// <summary>
/// Drives movement- and aim-related actions for AI using a <see cref="TopDownMotor"/>.
/// </summary>
public class MotorActuator : MonoBehaviour
{
    [SerializeField] private TopDownMotor motor;

    private void Awake()
    {
        if (!motor)
            motor = GetComponentInChildren<TopDownMotor>();
    }

    /// <summary>
    /// Move the character using a normalized direction and sprint toggle.
    /// </summary>
    public void Move(Vector3 direction, bool wantsSprint)
    {
        if (!motor)
            return;

        direction.y = 0f;
        if (direction.sqrMagnitude > 1f)
            direction = direction.normalized;

        motor.TickMove(direction, wantsSprint, Time.deltaTime);
    }

    /// <summary>
    /// Aim the character toward a world position.
    /// </summary>
    public void AimAt(Vector3 worldPosition)
    {
        if (!motor)
            return;

        if (motor.TryComputeYawFromPoint(worldPosition, out float yawDeg))
            motor.ApplyYaw(yawDeg, worldPosition);
    }

    /// <summary>
    /// Clear any aim targets so other systems can drive rotation.
    /// </summary>
    public void ClearAim()
    {
        if (motor)
            motor.ClearAimTargets();
    }
}
