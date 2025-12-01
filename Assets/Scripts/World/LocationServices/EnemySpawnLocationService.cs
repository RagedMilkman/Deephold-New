using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Determines where enemy spawners should live and registers them with the grid.
/// Keeps track of the chosen cells so other systems can query the spawn locations.
/// </summary>
[DefaultExecutionOrder(ServiceExecutionOrder.EnemySpawnLocation)]
public class EnemySpawnLocationService : MonoBehaviour
{
    [SerializeField] private GridDirector gridDirector;
    [SerializeField, Min(1)] private int desiredSpawnerCount = 7;

    private readonly List<Vector2Int> enemySpawnerCells = new();

    public IReadOnlyList<Vector2Int> EnemySpawnerCells => enemySpawnerCells;

    void Awake()
    {
        if (!gridDirector)
            gridDirector = FindFirstObjectByType<GridDirector>();
        if (!gridDirector)
            Debug.LogError("EnemySpawnLocationService: missing GridDirector reference");
    }

    /// <summary>
    /// Computes the initial enemy spawner layout and informs the grid so clients can build them.
    /// </summary>
    public void ServerInitializeSpawners(GridDirector grid)
    {
        if (grid)
            gridDirector = grid;

        if (!gridDirector)
        {
            Debug.LogError("EnemySpawnLocationService: cannot initialize without GridDirector");
            return;
        }

        enemySpawnerCells.Clear();

        var ring = BuildInnerRing();
        if (ring.Count == 0 || desiredSpawnerCount <= 0)
            return;

        int ringCount = ring.Count;
        int count = Mathf.Clamp(desiredSpawnerCount, 1, ringCount);
        float spacing = ringCount / (float)count;
        float position = spacing * 0.5f;

        var used = new HashSet<int>();

        for (int i = 0; i < count; i++)
        {
            int idx = Mathf.FloorToInt(position) % ringCount;
            int attempts = 0;
            bool placed = false;

            while (attempts < ringCount && !placed)
            {
                if (!used.Contains(idx))
                {
                    var candidate = ring[idx];
                    var cell = gridDirector.GetCell(candidate.x, candidate.y);

                    if (cell.type != CellType.Solid)
                    {
                        used.Add(idx);
                        enemySpawnerCells.Add(candidate);
                        placed = true;
                    }
                    else
                    {
                        used.Add(idx);
                    }
                }

                idx = (idx + 1) % ringCount;
                attempts++;
            }

            if (!placed)
                break;

            position += spacing;
        }

        if (enemySpawnerCells.Count > 0)
            gridDirector.ServerRegisterEnemySpawners(enemySpawnerCells);
    }

    List<Vector2Int> BuildInnerRing()
    {
        var ring = new List<Vector2Int>();
        if (!gridDirector)
            return ring;

        int width = gridDirector.Width;
        int height = gridDirector.Height;

        if (width < 3 || height < 3)
            return ring;

        for (int x = 1; x < width - 1; x++) ring.Add(new Vector2Int(x, 1));
        for (int y = 2; y < height - 1; y++) ring.Add(new Vector2Int(width - 2, y));
        for (int x = width - 3; x >= 1; x--) ring.Add(new Vector2Int(x, height - 2));
        for (int y = height - 3; y >= 2; y--) ring.Add(new Vector2Int(1, y));

        return ring;
    }
}
