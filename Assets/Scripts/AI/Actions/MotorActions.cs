using UnityEngine;

/// <summary>
/// High-level movement and facing helpers that delegate to a <see cref="MotorActuator"/>.
/// </summary>
public class MotorActions : MonoBehaviour
{
    [SerializeField] private MotorActuator motorActuator;
    [SerializeField, Min(0f)] private float aimTargetElevation = 1f;

    [Header("Debug")]
    [SerializeField] private bool debugDrawTarget;
    [SerializeField] private Color debugTargetColor = Color.cyan;
    [SerializeField, Min(0f)] private float debugTargetRadius = 0.15f;
    [SerializeField] private Color debugStopRangeColor = new Color(0f, 1f, 1f, 0.35f);

    private bool hasDebugTarget;
    private Vector3 debugTargetPosition;
    private float debugStopDistance;

    private void Awake()
    {
        if (!motorActuator)
            motorActuator = GetComponentInChildren<MotorActuator>();
    }

    /// <summary>
    /// Move toward a world-space position and optionally keep aiming at it.
    /// Returns true when the destination is reached within <paramref name="stopDistance"/>.
    /// </summary>
    public bool MoveToPosition(Vector3 currentPosition, Vector3 targetPosition, bool faceTarget, bool wantsSprint, float stopDistance = 0.1f)
    {
        UpdateDebugTarget(targetPosition, stopDistance);

        if (!motorActuator)
            return false;

        Vector3 toTarget = targetPosition - currentPosition;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude <= stopDistance * stopDistance)
        {
            if (!faceTarget)
                motorActuator.ClearAim();
            else
                motorActuator.AimAt(GetAimTarget(targetPosition));

            ClearDebugTarget();

            return true;
        }

        Vector3 direction = toTarget.normalized;
        motorActuator.Move(direction, wantsSprint);

        if (faceTarget)
            motorActuator.AimAt(GetAimTarget(targetPosition));

        return false;
    }

    /// <summary>
    /// Step toward the current path waypoint. Returns the updated waypoint index.
    /// </summary>
    public int MoveToPathPosition(Vector3 currentPosition, Vector2[] path, int currentIndex, bool faceTarget, bool wantsSprint, float waypointTolerance = 0.1f)
    {
        if (!motorActuator || path == null || path.Length == 0)
            return currentIndex;

        currentIndex = Mathf.Clamp(currentIndex, 0, path.Length - 1);
        Vector2 waypoint = path[currentIndex];
        Vector3 waypoint3D = new Vector3(waypoint.x, currentPosition.y, waypoint.y);

        bool reached = MoveToPosition(currentPosition, waypoint3D, faceTarget, wantsSprint, waypointTolerance);
        if (reached && currentIndex < path.Length - 1)
            currentIndex++;

        return currentIndex;
    }

    /// <summary>
    /// Rotate toward the desired waypoint without translating.
    /// </summary>
    public void RotateToPathPosition(Vector3 currentPosition, Vector2[] path, int currentIndex)
    {
        if (!motorActuator || path == null || path.Length == 0)
            return;

        currentIndex = Mathf.Clamp(currentIndex, 0, path.Length - 1);
        Vector2 waypoint = path[currentIndex];
        Vector3 waypoint3D = new Vector3(waypoint.x, currentPosition.y, waypoint.y);
        motorActuator.AimAt(GetAimTarget(waypoint3D));
    }

    /// <summary>
    /// Aim toward a direction from the current position without applying translation.
    /// </summary>
    public void AimFromPosition(Vector3 originPosition, Vector3 lookDirection)
    {
        if (!motorActuator)
            return;

        lookDirection.y = 0f;
        if (lookDirection.sqrMagnitude < 0.0001f)
            return;

        Vector3 target = originPosition + lookDirection.normalized;
        motorActuator.AimAt(GetAimTarget(target));
    }

    private Vector3 GetAimTarget(Vector3 targetPosition)
    {
        if (aimTargetElevation <= 0f)
            return targetPosition;

        return targetPosition + Vector3.up * aimTargetElevation;
    }

    /// <summary>
    /// Rotate to directly face a target transform.
    /// </summary>
    public void RotateToTarget(Transform target)
    {
        if (!motorActuator || !target)
            return;

        motorActuator.AimAt(target.position);
    }

    private void UpdateDebugTarget(Vector3 targetPosition, float stopDistance)
    {
        if (!debugDrawTarget)
            return;

        hasDebugTarget = true;
        debugTargetPosition = targetPosition;
        debugStopDistance = Mathf.Max(0f, stopDistance);
    }

    private void ClearDebugTarget()
    {
        if (!debugDrawTarget)
            return;

        hasDebugTarget = false;
    }

    private void OnDrawGizmosSelected()
    {
        if (!debugDrawTarget || !hasDebugTarget)
            return;

        Gizmos.color = debugTargetColor;
        Gizmos.DrawSphere(debugTargetPosition, debugTargetRadius);

        if (debugStopDistance > 0f && debugStopRangeColor.a > 0f)
        {
            Gizmos.color = debugStopRangeColor;
            Gizmos.DrawWireSphere(debugTargetPosition, debugStopDistance);
        }
    }
}
