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
    [SerializeField] private float _stopDistance = 0.5f;
    [SerializeField] [Min(0f)] private float _minLookDistance = 1.5f;

    private CharacterController _controller;

    private void Awake()
    {
        if (!_motor) _motor = GetComponentInChildren<TopDownMotor>();

        // Use the transform that actually moves.
        _controller =
            (_motor ? _motor.GetComponent<CharacterController>() : null) ??
            (_motor ? _motor.GetComponentInChildren<CharacterController>() : null) ??
            GetComponentInChildren<CharacterController>();
    }

    private void Update()
    { 
        if (!_motor || !_target || state == null || state.State == LifeState.Dead)
            return;

        Vector3 origin = _controller ? _controller.transform.position : _motor.transform.position;

        Vector3 toTarget = _target.position - origin;
        toTarget.y = 0f;

        float stopSqr = _stopDistance * _stopDistance;
        bool shouldMove = toTarget.sqrMagnitude > stopSqr;

        Vector3 moveDirection = shouldMove && toTarget.sqrMagnitude > 0.0001f
            ? toTarget.normalized
            : Vector3.zero;

        _motor.TickMove(moveDirection, false, Time.deltaTime);

        // Flatten look point to reduce vertical jitter/odd yaw inputs
        Vector3 lookPoint = _target.position;
        lookPoint.y = origin.y;

        Vector3 lookDirection = lookPoint - origin;
        if (lookDirection.sqrMagnitude > 0.0001f)
        {
            lookPoint = origin + lookDirection.normalized * Mathf.Max(_minLookDistance, lookDirection.magnitude);
        }

        if (_motor.TryComputeYawFromPoint(lookPoint, out float yawDeg))
            _motor.ApplyYaw(yawDeg, lookPoint);
    }
}
