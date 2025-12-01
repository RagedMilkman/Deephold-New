using UnityEngine;

/// <summary>
/// Simple AI driver that uses <see cref="TopDownMotor"/> to move towards and face a target transform.
/// </summary>
public class TopDownChaseAI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TopDownMotor _motor;
    [SerializeField] private Transform _target;

    [Header("Behavior")]
    [SerializeField, Tooltip("Distance from the target where the AI stops moving.")]
    private float _stopDistance = 0.5f;

    private void Awake()
    {
        if (!_motor) _motor = GetComponentInChildren<TopDownMotor>();
    }

    private void Update()
    {
        if (!_motor || !_target)
            return;

        Vector3 toTarget = _target.position - _motor.transform.position;
        toTarget.y = 0f;

        bool shouldMove = toTarget.sqrMagnitude > (_stopDistance * _stopDistance);
        Vector3 moveDirection = shouldMove ? toTarget.normalized : Vector3.zero;

        _motor.TickMove(moveDirection, false, Time.deltaTime);

        if (_motor.TryComputeYawFromPoint(_target.position, out float yawDeg))
        {
            _motor.ApplyYaw(yawDeg, _target.position);
        }
    }
}
