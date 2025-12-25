using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Behaviour base that offers helpers for building and following navigation paths.
/// </summary>
public abstract class PathingBehaviour : BehaviourBase
{
    [Header("Dependencies")]
    [SerializeField] protected MotorActions motorActions;
    [SerializeField] protected PathingService pathingService;
    [SerializeField] protected GridDirector gridDirector;
    [SerializeField] protected CharacterController characterController;

    [Header("Pathing")]
    [SerializeField, Min(0f)] protected float waypointTolerance = 0.15f;

    [Header("Debug")]
    [SerializeField] private bool debugDrawPath;
    [SerializeField] private Color debugPathColor = Color.green;
    [SerializeField, Min(0f)] private float debugNodeRadius = 0.07f;
    [SerializeField] private Color debugDestinationColor = Color.yellow;

    private readonly List<Vector2Int> pathCells = new();
    private Vector3[] debugPath = Array.Empty<Vector3>();
    private bool hasDebugPath;

    protected Vector3 CurrentPosition => characterController ? characterController.transform.position : transform.position;

    protected virtual void Awake()
    {
        if (!motorActions)
            motorActions = GetComponentInChildren<MotorActions>();

        if (!pathingService)
            pathingService = FindFirstObjectByType<PathingService>();

        if (!gridDirector)
            gridDirector = FindFirstObjectByType<GridDirector>();

        if (!characterController)
            characterController = GetComponentInParent<CharacterController>();
    }

    /// <summary>
    /// Builds a path from the behaviour's transform to the destination, using the
    /// pathing service when possible and falling back to a straight line.
    /// </summary>
    protected Vector2[] BuildPath(Vector3 destination)
    {
        if (pathingService && gridDirector)
        {
            Vector3 origin = CurrentPosition;
            if (gridDirector.TryWorldToCell(origin, out int startX, out int startY)
                && gridDirector.TryWorldToCell(destination, out int destX, out int destY))
            {
                pathCells.Clear();
                if (pathingService.TryGetPath(new PathingService.PathRequest(new Vector2Int(startX, startY), new Vector2Int(destX, destY)), pathCells)
                    && pathCells.Count > 0)
                {
                    return ConvertCellsToPath(pathCells);
                }
            }
        }

        return new[]
        {
            new Vector2(CurrentPosition.x, CurrentPosition.z),
            new Vector2(destination.x, destination.z)
        };
    }

    /// <summary>
    /// Attempts to snap a desired world position to a navigable cell center that exists on the map.
    /// </summary>
    protected bool TryResolveDestination(Vector3 desiredWorld, out Vector3 destination)
    {
        destination = desiredWorld;

        if (!gridDirector)
            return false;

        float cellSize = gridDirector.CellSize;
        int clampedX = Mathf.Clamp(Mathf.FloorToInt(desiredWorld.x / cellSize), 0, gridDirector.Width - 1);
        int clampedY = Mathf.Clamp(Mathf.FloorToInt(desiredWorld.z / cellSize), 0, gridDirector.Height - 1);

        var cell = gridDirector.GetCell(clampedX, clampedY);
        if (!cell.IsConsideredForPathfinding)
            return false;

        destination = gridDirector.CellToWorldCenter(clampedX, clampedY);
        return true;
    }

    private Vector2[] ConvertCellsToPath(List<Vector2Int> cells)
    {
        var path = new Vector2[cells.Count];
        for (int i = 0; i < cells.Count; i++)
        {
            Vector2Int cell = cells[i];
            Vector3 world = gridDirector.CellToWorldCenter(cell.x, cell.y);
            path[i] = new Vector2(world.x, world.z);
        }

        return path;
    }

    protected void UpdateDebugPath(Vector2[] path)
    {
        if (!debugDrawPath || path == null || path.Length == 0)
        {
            ClearDebugPath();
            return;
        }

        hasDebugPath = true;

        if (debugPath.Length != path.Length)
            debugPath = new Vector3[path.Length];

        float y = CurrentPosition.y;
        for (int i = 0; i < path.Length; i++)
            debugPath[i] = new Vector3(path[i].x, y, path[i].y);
    }

    protected void ClearDebugPath()
    {
        if (!debugDrawPath)
            return;

        hasDebugPath = false;
    }

    private void OnDrawGizmosSelected()
    {
        if (!debugDrawPath || !hasDebugPath || debugPath == null || debugPath.Length == 0)
            return;

        Gizmos.color = debugPathColor;
        for (int i = 0; i < debugPath.Length; i++)
        {
            Vector3 point = debugPath[i];
            Gizmos.DrawSphere(point, debugNodeRadius);

            if (i < debugPath.Length - 1)
                Gizmos.DrawLine(point, debugPath[i + 1]);
        }

        Gizmos.color = debugDestinationColor;
        Gizmos.DrawSphere(debugPath[^1], debugNodeRadius * 1.25f);
    }
}
