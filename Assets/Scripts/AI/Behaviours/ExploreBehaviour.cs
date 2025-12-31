using System;
using Unity.VisualScripting;
using UnityEngine;

/// <summary>
/// Behaviour that walks the agent through exploration waypoints.
/// </summary>
public class ExploreBehaviour : PathingBehaviour
{
    [SerializeField] private bool sprintWhileExploring;
    [SerializeField] private bool faceMovementDirection = true;
    [SerializeField, Min(0f)] private float lookHeight = 1.5f;
    [SerializeField, Min(0f)] private float minLookDuration = 0.5f;
    [SerializeField, Min(0f)] private float maxLookDuration = 1.25f;
    [SerializeField, Min(1)] private int lookStepsPerDecision = 3;

    private enum ExploreState
    {
        Looking,
        Traveling
    }

    private ExploreState state = ExploreState.Looking;
    private Vector2[] path = Array.Empty<Vector2>();
    private int pathIndex;
    private Vector3? resolvedDestination;
    private Vector3? queuedTravelDirection;
    private float lookTimer;
    private float lookDuration;
    private int currentLookIndex;
    private Vector3[] lookDirections = Array.Empty<Vector3>();
    private bool HasActivePath => resolvedDestination.HasValue && path != null && path.Length > 0;

    public override IntentType IntentType => IntentType.Explore;

    public override void BeginBehaviour(IIntent intent)
    {
        pathIndex = 0;
        StartLooking(intent as ExploreIntent);
    }

    public override void TickBehaviour(IIntent intent)
    {
        if (motorActions == null)
            return;

        var exploreIntent = intent as ExploreIntent;
        if (exploreIntent == null)
            return;

        if (state == ExploreState.Looking)
        {
            TickLooking(exploreIntent);
        }
        else
        {
            TickTravel(exploreIntent);
        }
    }

    public override void EndBehaviour()
    {
        path = Array.Empty<Vector2>();
        pathIndex = 0;
        resolvedDestination = null;
        queuedTravelDirection = null;
        lookTimer = 0f;
        lookDuration = 0f;
        currentLookIndex = 0;
        lookDirections = Array.Empty<Vector3>();
        ClearDebugPath();
    }

    private void TickLooking(ExploreIntent intent)
    {
        if (lookDirections == null || lookDirections.Length == 0)
            PrepareLookDirections(intent);

        if (lookDirections.Length == 0)
        {
            BeginTravel(intent);
            return;
        }

        currentLookIndex = Mathf.Clamp(currentLookIndex, 0, lookDirections.Length - 1);
        motorActions.RotateToTarget(lookDirections[currentLookIndex]);

        lookTimer += Time.deltaTime;
        if (lookTimer >= lookDuration)
        {
            lookTimer = 0f;
            lookDuration = GetNextLookDuration();
            currentLookIndex++;

            if (currentLookIndex >= lookDirections.Length)
            {
                queuedTravelDirection = lookDirections[^1];
                BeginTravel(intent);
            }
        }
    }

    private void TickTravel(ExploreIntent intent)
    {
        if (!HasActivePath)
        {
            StartLooking(intent);
            return;
        }

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

                StartLooking(intent);
            }
        }
    }

    private void BeginTravel(ExploreIntent intent)
    {
        RebuildPath(intent);
        pathIndex = 0;
        if (HasActivePath)
        {
            state = ExploreState.Traveling;
        }
        else
        {
            StartLooking(intent);
        }
    }

    private void StartLooking(ExploreIntent intent)
    {
        state = ExploreState.Looking;
        path = Array.Empty<Vector2>();
        resolvedDestination = null;
        queuedTravelDirection = null;
        lookTimer = 0f;
        lookDuration = GetNextLookDuration();
        currentLookIndex = 0;
        PrepareLookDirections(intent, null);
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

        resolvedDestination = destination;

        path = BuildPath(destination);
        UpdateDebugPath(path);
    }

    private bool TryFindDestination(ExploreIntent intent, out Vector3 destination)
    {
        destination = Vector3.zero;

        var direction = queuedTravelDirection ?? intent.DesiredDirection;
        direction = FlattenDirection(direction);
        float distance = Mathf.Max(intent.DesiredDistance, waypointTolerance * 2f);
        var origin = CurrentPosition;

        if (direction.sqrMagnitude < 0.0001f)
            direction = UnityEngine.Random.insideUnitSphere;

        direction.y = lookHeight;

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
            randomDirection.y = lookHeight;

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
        direction.y = lookHeight;
        return direction;
    }

    private void PrepareLookDirections(ExploreIntent intent, Transform currenPosition)
    {
        int lookCount = Mathf.Max(1, lookStepsPerDecision);
        if (lookDirections == null || lookDirections.Length != lookCount)
            lookDirections = new Vector3[lookCount];

        Vector3 baseDirection = intent != null ? intent.DesiredDirection : Vector3.zero;
        if (baseDirection.sqrMagnitude < 0.0001f)
            baseDirection = UnityEngine.Random.insideUnitSphere;

        baseDirection = FlattenDirection(baseDirection);
        if (baseDirection.sqrMagnitude < 0.0001f)
            baseDirection = Vector3.forward;

        lookDirections[0] = baseDirection;
        for (int i = 1; i < lookCount; i++)
        {
            var randomDirection = UnityEngine.Random.insideUnitSphere;
            randomDirection.y = lookHeight;

            if (randomDirection.sqrMagnitude < 0.0001f)
                randomDirection = UnityEngine.Random.onUnitSphere;

            randomDirection.y = lookHeight;
            lookDirections[i] = currenPosition.position + randomDirection.normalized * 4;
        }
    }

    private float GetNextLookDuration()
    {
        float min = Mathf.Max(0f, minLookDuration);
        float max = Mathf.Max(min, maxLookDuration);
        return UnityEngine.Random.Range(min, max);
    }
}
