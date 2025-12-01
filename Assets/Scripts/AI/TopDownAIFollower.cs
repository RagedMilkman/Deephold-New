using UnityEngine;

/// <summary>
/// Simple AI driver that moves a TopDownMotor toward a target and keeps facing it.
/// </summary>
public class TopDownAIFollower : MonoBehaviour
{
    [SerializeField] private TopDownMotor _motor;
    [SerializeField] private Transform _target;
    [SerializeField] private float _stopRange = 1.5f;
    [Header("Debug")]
    [SerializeField] private bool _showDebugGizmos = false;
    [SerializeField] private bool _debugOnlyWhenSelected = true;
    [SerializeField] private Color _moveDirectionColor = Color.cyan;
    [SerializeField] private Color _lookDirectionColor = Color.yellow;
    [SerializeField] private Color _stopRangeColor = new Color(1f, 0.5f, 0f, 0.5f);

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

    private void OnDrawGizmos()
    {
        DrawDebugGizmos(false);
    }

    private void OnDrawGizmosSelected()
    {
        DrawDebugGizmos(true);
    }

    private void DrawDebugGizmos(bool isSelected)
    {
        if (!_showDebugGizmos || (_debugOnlyWhenSelected && !isSelected)) return;

        if (!_motor) _motor = GetComponent<TopDownMotor>();
        if (!_motor) return;

        Vector3 origin = _motor.transform.position;

        if (_target)
        {
            Vector3 toTarget = _target.position - origin;
            toTarget.y = 0f;

            Gizmos.color = _stopRangeColor;
            Gizmos.DrawWireSphere(origin, _stopRange);

            float stopRangeSqr = _stopRange * _stopRange;
            if (toTarget.sqrMagnitude > stopRangeSqr)
            {
                Vector3 direction = toTarget.normalized;
                Vector3 moveDirection = new Vector3(direction.x, 0f, direction.z);
                Gizmos.color = _moveDirectionColor;
                Gizmos.DrawLine(origin, origin + moveDirection);
                Gizmos.DrawSphere(origin + moveDirection, 0.05f);
            }

            Gizmos.color = _lookDirectionColor;
            Gizmos.DrawLine(origin, origin + toTarget.normalized);
            Gizmos.DrawSphere(origin + toTarget.normalized, 0.05f);
        }
    }
}
