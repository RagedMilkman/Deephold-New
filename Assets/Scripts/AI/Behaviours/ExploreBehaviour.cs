using System;
using UnityEngine;

/// <summary>
/// Behaviour that walks the agent through exploration waypoints.
/// </summary>
public class ExploreBehaviour : PathingBehaviour
{
    [SerializeField] private bool sprintWhileExploring;
    [SerializeField] private bool faceMovementDirection = true;

    private Vector2[] path = Array.Empty<Vector2>();
    private int pathIndex;
    private Vector3? resolvedDestination;
    private Vector3? lastDesiredDirection;
    private float lastDesiredDistance;

    public override IntentType IntentType => IntentType.Explore;

    public override void BeginBehaviour(IIntent intent)
    {
        pathIndex = 0;
        RebuildPath(intent as ExploreIntent);
    }

    public override void TickBehaviour(IIntent intent)
    {
        if (motorActions == null)
            return;

        var exploreIntent = intent as ExploreIntent;
        if (exploreIntent == null)
            return;

        if (IntentChanged(exploreIntent) || !resolvedDestination.HasValue)
        {
            RebuildPath(exploreIntent);
            pathIndex = 0;
        }

        if (path == null || path.Length == 0)
            return;

        var currentPosition = CurrentPosition;
        pathIndex = motorActions.MoveToPathPosition(currentPosition, path, pathIndex, faceMovementDirection, sprintWhileExploring, waypointTolerance);

        if (pathIndex >= path.Length - 1)
        {
            var finalTarget = new Vector3(path[^1].x, currentPosition.y, path[^1].y);
            bool reached = motorActions.MoveToPosition(currentPosition, finalTarget, faceMovementDirection, sprintWhileExploring, waypointTolerance);
            if (reached)
            {
                path = Array.Empty<Vector2>();
                pathIndex = 0;
                resolvedDestination = null;
                ClearDebugPath();
            }
        }
    }

    public override void EndBehaviour()
    {
        path = Array.Empty<Vector2>();
        pathIndex = 0;
        resolvedDestination = null;
        lastDesiredDirection = null;
        lastDesiredDistance = 0f;
        ClearDebugPath();
    }

    private void RebuildPath(ExploreIntent intent)
    {
        path = Array.Empty<Vector2>();
        resolvedDestination = null;
        ClearDebugPath();

        if (intent == null)
            return;

        if (!TryFindDestination(intent, out var destination))
            return;

        lastDesiredDirection = FlattenDirection(intent.DesiredDirection);
        lastDesiredDistance = Mathf.Max(0f, intent.DesiredDistance);
        resolvedDestination = destination;

        path = BuildPath(destination);
        UpdateDebugPath(path);
    }

    private bool IntentChanged(ExploreIntent intent)
    {
        if (intent == null)
            return false;

        var direction = FlattenDirection(intent.DesiredDirection);
        float distance = Mathf.Max(0f, intent.DesiredDistance);

        bool directionChanged = !lastDesiredDirection.HasValue
                                 || Vector3.SqrMagnitude(direction - lastDesiredDirection.Value) > 0.01f;
        bool distanceChanged = Mathf.Abs(distance - lastDesiredDistance) > 0.01f;

        return directionChanged || distanceChanged;
    }

    private bool TryFindDestination(ExploreIntent intent, out Vector3 destination)
    {
        destination = Vector3.zero;

        var direction = FlattenDirection(intent.DesiredDirection);
        float distance = Mathf.Max(intent.DesiredDistance, waypointTolerance * 2f);
        var origin = CurrentPosition;

        if (direction.sqrMagnitude < 0.0001f)
            direction = UnityEngine.Random.insideUnitSphere;

        direction.y = 0f;

        if (direction.sqrMagnitude > 0.0001f)
        {
            var desired = origin + direction.normalized * distance;
            desired.y = origin.y;

            if (TryResolveDestination(desired, out destination))
                return true;
        }

        for (int i = 0; i < 4; i++)
        {
            var randomDirection = UnityEngine.Random.insideUnitSphere;
            randomDirection.y = 0f;

            if (randomDirection.sqrMagnitude < 0.0001f)
                continue;

            var candidate = origin + randomDirection.normalized * distance;
            candidate.y = origin.y;

            if (TryResolveDestination(candidate, out destination))
                return true;
        }

        return false;
    }

    private Vector3 FlattenDirection(Vector3 direction)
    {
        direction.y = 0f;
        return direction;
    }
}
