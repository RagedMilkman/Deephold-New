using System;
using UnityEngine;

/// <summary>
/// Behaviour that runs along a path away from threats.
/// </summary>
public class FleeBehaviour : PathingBehaviour
{
    private Vector2[] path = Array.Empty<Vector2>();
    private int pathIndex;
    private Vector3? resolvedEscapeTarget;
    private Vector3? lastEscapeDirection;
    private float lastEscapeDistance;
    private Vector3? lastThreatPosition;

    public override IntentType IntentType => IntentType.Flee;

    public override void BeginBehaviour(IIntent intent)
    {
        pathIndex = 0;
        RebuildPath(intent as FleeIntent);
    }

    public override void TickBehaviour(IIntent intent)
    {
        if (motorActions == null)
            return;

        var fleeIntent = intent as FleeIntent;
        if (fleeIntent == null)
            return;

        if (IntentChanged(fleeIntent) || !resolvedEscapeTarget.HasValue)
        {
            RebuildPath(fleeIntent);
            pathIndex = 0;
        }

        if (path == null || path.Length == 0)
            return;

        var currentPosition = CurrentPosition;
        pathIndex = motorActions.MoveToPathPosition(currentPosition, path, pathIndex, true, true, waypointTolerance);

        if (pathIndex >= path.Length - 1)
        {
            var finalTarget = new Vector3(path[^1].x, currentPosition.y, path[^1].y);
            bool reached = motorActions.MoveToPosition(currentPosition, finalTarget, true, true, waypointTolerance);
            if (reached)
            {
                path = Array.Empty<Vector2>();
                pathIndex = 0;
                resolvedEscapeTarget = null;
                ClearDebugPath();
            }
        }
    }

    public override void EndBehaviour()
    {
        path = Array.Empty<Vector2>();
        pathIndex = 0;
        resolvedEscapeTarget = null;
        lastEscapeDirection = null;
        lastEscapeDistance = 0f;
        lastThreatPosition = null;
        ClearDebugPath();
    }

    private void RebuildPath(FleeIntent intent)
    {
        path = Array.Empty<Vector2>();
        resolvedEscapeTarget = null;
        ClearDebugPath();

        if (intent == null)
            return;

        if (!TryFindEscapeTarget(intent, out var destination))
            return;

        lastEscapeDirection = FlattenDirection(intent.EscapeDirection);
        lastEscapeDistance = Mathf.Max(0f, intent.EscapeDistance);
        lastThreatPosition = intent.ThreatPosition;
        resolvedEscapeTarget = destination;

        path = BuildPath(destination);
        UpdateDebugPath(path);
    }

    private bool IntentChanged(FleeIntent intent)
    {
        if (intent == null)
            return false;

        var direction = FlattenDirection(intent.EscapeDirection);
        float distance = Mathf.Max(0f, intent.EscapeDistance);
        var threatPosition = intent.ThreatPosition;

        bool directionChanged = !lastEscapeDirection.HasValue
                                 || Vector3.SqrMagnitude(direction - lastEscapeDirection.Value) > 0.01f;
        bool distanceChanged = Mathf.Abs(distance - lastEscapeDistance) > 0.01f;
        bool threatChanged = !lastThreatPosition.HasValue
                             || Vector3.SqrMagnitude(threatPosition - lastThreatPosition.Value) > 0.01f;

        return directionChanged || distanceChanged || threatChanged;
    }

    private bool TryFindEscapeTarget(FleeIntent intent, out Vector3 destination)
    {
        destination = Vector3.zero;

        var direction = FlattenDirection(intent.EscapeDirection);
        if (direction == Vector3.zero && intent.ThreatPosition != Vector3.zero)
        {
            direction = FlattenDirection(CurrentPosition - intent.ThreatPosition);
        }
        float distance = Mathf.Max(intent.EscapeDistance, waypointTolerance * 2f);
        var origin = CurrentPosition;

        if (direction.sqrMagnitude > 0.0001f)
        {
            var desired = origin + direction.normalized * distance;
            desired.y = origin.y;

            if (TryResolveDestination(desired, out destination))
                return true;

            for (int i = 2; i >= 1; i--)
            {
                var shortened = origin + direction.normalized * (distance * i / 3f);
                shortened.y = origin.y;

                if (TryResolveDestination(shortened, out destination))
                    return true;
            }
        }

        return false;
    }

    private Vector3 FlattenDirection(Vector3 direction)
    {
        direction.y = 0f;
        return direction;
    }
}
