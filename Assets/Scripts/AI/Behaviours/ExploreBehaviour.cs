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
    private Vector3? lastDestination;

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

        if (!lastDestination.HasValue || (exploreIntent.Destination - lastDestination.Value).sqrMagnitude > 0.01f)
        {
            RebuildPath(exploreIntent);
            pathIndex = 0;
        }

        if (path == null || path.Length == 0)
            return;

        var currentPosition = transform.position;
        pathIndex = motorActions.MoveToPathPosition(currentPosition, path, pathIndex, faceMovementDirection, sprintWhileExploring, waypointTolerance);

        if (pathIndex >= path.Length - 1)
        {
            var finalTarget = new Vector3(path[^1].x, currentPosition.y, path[^1].y);
            bool reached = motorActions.MoveToPosition(currentPosition, finalTarget, faceMovementDirection, sprintWhileExploring, waypointTolerance);
            if (reached)
            {
                path = Array.Empty<Vector2>();
                pathIndex = 0;
                ClearDebugPath();
            }
        }
    }

    public override void EndBehaviour()
    {
        path = Array.Empty<Vector2>();
        pathIndex = 0;
        lastDestination = null;
        ClearDebugPath();
    }

    private void RebuildPath(ExploreIntent intent)
    {
        if (intent == null)
        {
            path = Array.Empty<Vector2>();
            lastDestination = null;
            ClearDebugPath();
            return;
        }

        lastDestination = intent.Destination;
        path = BuildPath(intent.Destination);
        UpdateDebugPath(path);
    }
}
