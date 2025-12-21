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

    [Header("Pathing")]
    [SerializeField, Min(0f)] protected float waypointTolerance = 0.15f;

    private readonly List<Vector2Int> pathCells = new();

    protected virtual void Awake()
    {
        if (!motorActions)
            motorActions = GetComponentInChildren<MotorActions>();

        if (!pathingService)
            pathingService = FindFirstObjectByType<PathingService>();

        if (!gridDirector)
            gridDirector = FindFirstObjectByType<GridDirector>();
    }

    /// <summary>
    /// Builds a path from the behaviour's transform to the destination, using the
    /// pathing service when possible and falling back to a straight line.
    /// </summary>
    protected Vector2[] BuildPath(Vector3 destination)
    {
        if (pathingService && gridDirector)
        {
            if (gridDirector.TryWorldToCell(transform.position, out int startX, out int startY)
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
            new Vector2(transform.position.x, transform.position.z),
            new Vector2(destination.x, destination.z)
        };
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
}
