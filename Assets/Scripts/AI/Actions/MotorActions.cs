using UnityEngine;

/// <summary>
/// High-level movement and facing helpers that delegate to a <see cref="MotorActuator"/>.
/// </summary>
public class MotorActions : MonoBehaviour
{
    [SerializeField] private MotorActuator motorActuator;

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
        if (!motorActuator)
            return false;

        Vector3 toTarget = targetPosition - currentPosition;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude <= stopDistance * stopDistance)
        {
            if (!faceTarget)
                motorActuator.ClearAim();
            else
                motorActuator.AimAt(targetPosition);

            return true;
        }

        Vector3 direction = toTarget.normalized;
        motorActuator.Move(direction, wantsSprint);

        if (faceTarget)
            motorActuator.AimAt(targetPosition);

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
        motorActuator.AimAt(waypoint3D);
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
}
