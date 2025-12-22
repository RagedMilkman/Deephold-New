using UnityEngine;

/// <summary>
/// Simple AI driver that uses <see cref="TopDownMotor"/> to move towards and face a target transform.
/// </summary>
public class TopDownChaseAI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TopDownMotor _motor;
    [SerializeField] private Transform _target;
    [SerializeField] private CharacterHealth state;

    [Header("Behavior")]
    [SerializeField, Tooltip("Distance from the target where the AI stops moving.")]
    private float _stopDistance = 0.5f;
    [SerializeField, Tooltip("Clamp for the deltaTime passed to the motor to avoid large movement steps on slow frames.")]
    private float _maxTickDeltaTime = 0.05f;

    private Vector3 _desiredMoveDirection;

    private void Awake()
    {
        if (!_motor) _motor = GetComponentInChildren<TopDownMotor>();
    }

    private void Update()
    {
        if (!_motor || !_target || (state && state.State == LifeState.Dead))
            return;

        Vector3 toTarget = _target.position - _motor.transform.position;
        toTarget.y = 0f;

        bool shouldMove = toTarget.sqrMagnitude > (_stopDistance * _stopDistance);
        _desiredMoveDirection = shouldMove ? toTarget.normalized : Vector3.zero;

        if (_motor.TryComputeYawFromPoint(_target.position, out float yawDeg))
        {
            _motor.ApplyYaw(yawDeg, _target.position);
        }
    }

    private void FixedUpdate()
    {
        if (!_motor || !_target || (state && state.State == LifeState.Dead))
            return;

        float dt = Mathf.Min(Time.fixedDeltaTime, _maxTickDeltaTime);

        // Drive movement in fixed time so acceleration and CharacterController integration stay stable even when
        // the render loop hiccups. We use the last direction computed in Update to stay in sync with the current
        // target position and yaw.
        _motor.TickMove(_desiredMoveDirection, false, dt);
    }
}
