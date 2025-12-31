using System;
using System.Collections.Generic;
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
    [SerializeField, Min(1)] private int lookRegionRadius = 3;

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
    private readonly List<CellData> candidateCells = new();
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
        PrepareLookDirections(intent);
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

    private void PrepareLookDirections(ExploreIntent intent)
    {
        int lookCount = Mathf.Max(1, lookStepsPerDecision);
        if (lookDirections == null || lookDirections.Length != lookCount)
            lookDirections = new Vector3[lookCount];

        if (TryPopulateLookDirectionsFromGrid(lookCount))
            return;

        PopulateRandomLookDirections(intent, lookCount);
    }

    private bool TryPopulateLookDirectionsFromGrid(int lookCount)
    {
        if (!gridDirector)
            return false;

        if (!gridDirector.TryWorldToCell(CurrentPosition, out int cellX, out int cellY))
            return false;

        var region = new RectInt(cellX - lookRegionRadius, cellY - lookRegionRadius,
            lookRegionRadius * 2 + 1, lookRegionRadius * 2 + 1);

        var regionCells = gridDirector.GetRegion(region);
        if (regionCells == null || regionCells.Count == 0)
            return false;

        candidateCells.Clear();
        foreach (var cell in regionCells)
        {
            if (cell.IsConsideredForPathfinding)
                candidateCells.Add(cell);
        }

        if (candidateCells.Count == 0)
            return false;

        var origin = CurrentPosition;
        for (int i = 0; i < lookCount; i++)
        {
            var cell = candidateCells[UnityEngine.Random.Range(0, candidateCells.Count)];
            var target = gridDirector.CellToWorldCenter(cell.x, cell.y);
            target.y = origin.y + lookHeight;
            lookDirections[i] = target;
        }

        return true;
    }

    private void PopulateRandomLookDirections(ExploreIntent intent, int lookCount)
    {
        var origin = CurrentPosition;
        Vector3 baseDirection = intent != null ? intent.DesiredDirection : Vector3.zero;
        if (baseDirection.sqrMagnitude < 0.0001f)
            baseDirection = UnityEngine.Random.insideUnitSphere;

        baseDirection.y = 0f;
        if (baseDirection.sqrMagnitude < 0.0001f)
            baseDirection = Vector3.forward;

        var desiredDistance = Mathf.Max(intent?.DesiredDistance ?? 1f, waypointTolerance * 2f);
        var baseTarget = origin + baseDirection.normalized * desiredDistance;
        baseTarget.y = origin.y + lookHeight;
        lookDirections[0] = baseTarget;

        for (int i = 1; i < lookCount; i++)
        {
            var randomDirection = UnityEngine.Random.insideUnitSphere;
            randomDirection.y = 0f;

            if (randomDirection.sqrMagnitude < 0.0001f)
                randomDirection = UnityEngine.Random.onUnitSphere;

            randomDirection.y = 0f;
            var target = origin + randomDirection.normalized * Mathf.Max(lookHeight * 2f, 3f);
            target.y = origin.y + lookHeight;
            lookDirections[i] = target;
        }
    }

    private float GetNextLookDuration()
    {
        float min = Mathf.Max(0f, minLookDuration);
        float max = Mathf.Max(min, maxLookDuration);
        return UnityEngine.Random.Range(min, max);
    }
}
