using System;
using UnityEngine;

/// <summary>
/// Behaviour that runs along a path away from threats.
/// </summary>
public class FleeBehaviour : PathingBehaviour
{
    private Vector2[] path = Array.Empty<Vector2>();
    private int pathIndex;
    private Vector3? lastEscapeTarget;

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

        if (!lastEscapeTarget.HasValue || (fleeIntent.EscapePos - lastEscapeTarget.Value).sqrMagnitude > 0.01f)
        {
            RebuildPath(fleeIntent);
            pathIndex = 0;
        }

        if (path == null || path.Length == 0)
            return;

        var currentPosition = transform.position;
        pathIndex = motorActions.MoveToPathPosition(currentPosition, path, pathIndex, true, true, waypointTolerance);

        if (pathIndex >= path.Length - 1)
        {
            var finalTarget = new Vector3(path[^1].x, currentPosition.y, path[^1].y);
            bool reached = motorActions.MoveToPosition(currentPosition, finalTarget, true, true, waypointTolerance);
            if (reached)
            {
                path = Array.Empty<Vector2>();
                pathIndex = 0;
            }
        }
    }

    public override void EndBehaviour()
    {
        path = Array.Empty<Vector2>();
        pathIndex = 0;
        lastEscapeTarget = null;
    }

    private void RebuildPath(FleeIntent intent)
    {
        if (intent == null)
        {
            path = Array.Empty<Vector2>();
            lastEscapeTarget = null;
            return;
        }

        lastEscapeTarget = intent.EscapePos;
        path = BuildPath(intent.EscapePos);
    }
}
