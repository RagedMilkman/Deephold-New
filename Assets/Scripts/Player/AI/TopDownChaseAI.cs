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
    [SerializeField, Tooltip("Time in seconds to smooth sudden direction changes.")]
    private float _directionSmoothTime = 0.08f;
    [SerializeField, Tooltip("Clamp for the deltaTime passed to the motor to avoid large movement steps on slow frames.")]
    private float _maxTickDeltaTime = 0.05f;

    private Vector3 _smoothedMoveDirection;

    private void Awake()
    {
        if (!_motor) _motor = GetComponentInChildren<TopDownMotor>();
    }

    private void Update()
    {
        if (!_motor || !_target || (state && state.State == LifeState.Dead))
            return;

        float dt = Mathf.Min(Time.deltaTime, _maxTickDeltaTime);

        Vector3 toTarget = _target.position - _motor.transform.position;
        toTarget.y = 0f;

        bool shouldMove = toTarget.sqrMagnitude > (_stopDistance * _stopDistance);
        Vector3 desiredMoveDirection = shouldMove ? toTarget.normalized : Vector3.zero;

        float smoothingFactor = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, _directionSmoothTime));
        _smoothedMoveDirection = Vector3.Lerp(_smoothedMoveDirection, desiredMoveDirection, smoothingFactor);

        _motor.TickMove(_smoothedMoveDirection, false, dt);

        if (_motor.TryComputeYawFromPoint(_target.position, out float yawDeg))
        {
            _motor.ApplyYaw(yawDeg, _target.position);
        }
    }
}
