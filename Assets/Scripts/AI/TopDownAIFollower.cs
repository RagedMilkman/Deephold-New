using UnityEngine;

/// <summary>
/// Simple AI driver that moves a TopDownMotor toward a target and keeps facing it.
/// </summary>
public class TopDownAIFollower : MonoBehaviour
{
    [SerializeField] private TopDownMotor _motor;
    [SerializeField] private Transform _target;
    [SerializeField] private float _stopRange = 1.5f;

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

        Vector3 toTarget = _target.position - _motor.transform.position;
        toTarget.y = 0f;

        float stopRangeSqr = _stopRange * _stopRange;
        Vector2 input = Vector2.zero;

        if (toTarget.sqrMagnitude > stopRangeSqr)
        {
            Vector3 direction = toTarget.normalized;
            input = new Vector2(direction.x, direction.z);
        }

        _motor.TickMove(input, Time.deltaTime);

        if (_motor.TryComputeYawFromPoint(_target.position, out float yawDeg))
        {
            _motor.ApplyYaw(yawDeg, _target.position);
        }
    }
}
