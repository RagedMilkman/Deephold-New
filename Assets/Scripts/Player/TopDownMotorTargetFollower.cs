using UnityEngine;

/// <summary>
/// Simple helper for moving a <see cref="TopDownMotor"/> toward a target and keeping it faced at that point.
/// Intended for AI or scripted movement where input comes from a Transform instead of player controls.
/// </summary>
public class TopDownMotorTargetFollower : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TopDownMotor _motor;
    [SerializeField] private Transform _target;

    [Header("Tuning")]
    [SerializeField] private float _stopDistance = 0.5f;
    [SerializeField] private float _rotationSpeed = 720f;
    [SerializeField] private bool _sprintWhileMoving = false;
    [SerializeField] private bool _replicatePosition = false;

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
        if (_motor == null || _target == null)
            return;

        MoveTowardsTarget();
        FaceTarget();
    }

    private void MoveTowardsTarget()
    {
        Vector3 toTarget = _target.position - transform.position;
        toTarget.y = 0f;

        float stopDistanceSqr = _stopDistance * _stopDistance;
        Vector2 moveInput = Vector2.zero;

        if (toTarget.sqrMagnitude > stopDistanceSqr)
        {
            Vector3 direction = toTarget.normalized;
            moveInput = new Vector2(direction.x, direction.z);
        }

        _motor.TickMove(moveInput, _sprintWhileMoving, Time.deltaTime, _replicatePosition);
    }

    private void FaceTarget()
    {
        if (!_motor.TryComputeYawFromPoint(_target.position, out float targetYaw))
        {
            _motor.ClearAimTargets();
            return;
        }

        float currentYaw = transform.rotation.eulerAngles.y;
        float smoothedYaw = Mathf.MoveTowardsAngle(currentYaw, targetYaw, _rotationSpeed * Time.deltaTime);

        _motor.SetAimTargets(_target.position, _target.position);
        _motor.ApplyYaw(smoothedYaw, _target.position);
    }

    public void SetTarget(Transform target)
    {
        _target = target;
    }
}
