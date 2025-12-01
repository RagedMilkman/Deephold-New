using UnityEngine;

/// <summary>
/// Very basic AI that chases a target using <see cref="TopDownMotor"/>.
/// Moves toward the target until it is within the desired distance, backs up if too close,
/// and always faces the target.
/// </summary>
[RequireComponent(typeof(TopDownMotor))]
public class SimpleChaserAI : MonoBehaviour
{
    [SerializeField] private TopDownMotor _motor;
    [SerializeField] private Transform _target;
    [SerializeField] private float _desiredDistance = 2f;
    [SerializeField] private float _distanceBuffer = 0.1f;

    private void Reset()
    {
        if (!_motor) _motor = GetComponent<TopDownMotor>();
    }

    private void Awake()
    {
        if (!_motor) _motor = GetComponent<TopDownMotor>();
    }

    private void Update()
    {
        if (!_motor || !_target) return;

        Vector3 targetPosition = _target.position;
        Vector3 toTarget = targetPosition - transform.position;
        toTarget.y = 0f;

        float distance = toTarget.magnitude;
        if (distance < 0.001f)
        {
            _motor.TickMove(Vector2.zero, Time.deltaTime);
            return;
        }

        FaceTarget(targetPosition);

        Vector2 input = Vector2.zero;

        if (distance > _desiredDistance + _distanceBuffer)
        {
            input = WorldDirectionToMotorInput(toTarget.normalized);
        }
        else if (distance < _desiredDistance - _distanceBuffer)
        {
            input = WorldDirectionToMotorInput((-toTarget).normalized);
        }

        _motor.TickMove(input, Time.deltaTime);
    }

    private void FaceTarget(Vector3 targetPosition)
    {
        if (_motor.TryComputeYawFromPoint(targetPosition, out float yawDeg))
        {
            _motor.ApplyYaw(yawDeg, targetPosition);
        }
    }

    private Vector2 WorldDirectionToMotorInput(Vector3 worldDirection)
    {
        Transform reference = _motor ? _motor.transform : transform;
        Vector3 localDirection = reference.InverseTransformDirection(worldDirection);
        Vector2 input = new Vector2(localDirection.x, localDirection.z);
        return Vector2.ClampMagnitude(input, 1f);
    }
}
