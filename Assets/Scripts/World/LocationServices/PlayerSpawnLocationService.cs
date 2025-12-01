using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Decides *where* to spawn based on GridDirector data.
/// PlayerSpawnService consumes this service to get a position/rotation.
/// </summary>
[DefaultExecutionOrder(ServiceExecutionOrder.PlayerSpawnLocation)]
public class PlayerSpawnLocationService : MonoBehaviour
{
    [SerializeField] GridDirector grid;

    [Header("Strategy")]
    [SerializeField] int ringInner = 1;      // in cells, inner radius from center
    [SerializeField] int ringOuter = 12;     // in cells, outer radius from center
    [SerializeField] bool preferEdge = true; // prefer empty cells with more solid neighbors

    void Awake()
    {
        if (!grid) grid = FindFirstObjectByType<GridDirector>();
        if (!grid) Debug.LogError("PlayerSpawnLocationService: missing GridDirector");
    }

    /// <summary>
    /// Pick a spawn near the center, inside an annulus [ringInner, ringOuter].
    /// Use 'seed' to pick deterministically per player if desired.
    /// </summary>
    public bool TryPickSpawn(int seed, out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero; rot = Quaternion.identity;
        if (!grid) return false;

        // Build an annular region around center in grid coordinates
        var center = new Vector2Int(grid.Width / 2, grid.Height / 2);
        var region = AnnulusRect(center, ringOuter);
        var empties = FilterAnnulus(grid, center, ringInner, ringOuter);

        if (empties.Count == 0)
        {
            // Fallback: any empty cell in full grid
            empties = grid.GetEmptyInRegion(new RectInt(0, 0, grid.Width, grid.Height));
            if (empties.Count == 0) return false;
        }

        Vector2Int chosen = ChooseCell(empties, seed);
        pos = grid.CellToWorldCenter(chosen.x, chosen.y);
        pos.y = 1f;
        rot = Quaternion.identity; // Quaternion.Euler(0f, (seed * 37) % 360, 0f);
        return true;
    }

    // --------- Strategy helpers ---------

    RectInt AnnulusRect(Vector2Int center, int radius)
      => new RectInt(center.x - radius, center.y - radius, radius * 2 + 1, radius * 2 + 1);

    List<CellData> FilterAnnulus(GridDirector g, Vector2Int center, int inner, int outer)
    {
        var rect = AnnulusRect(center, outer);
        var all = g.GetRegion(rect);
        var list = new List<CellData>(all.Count);
        foreach (var c in all)
        {
            int dx = c.x - center.x;
            int dy = c.y - center.y;
            int manhattan = Mathf.Abs(dx) + Mathf.Abs(dy);
            if (manhattan < inner || manhattan > outer) continue;
            if (c.type == CellType.Empty) list.Add(c);
        }
        return list;
    }

    Vector2Int ChooseCell(List<CellData> candidates, int seed)
    {
        if (!preferEdge || candidates.Count == 0)
            return new Vector2Int(candidates[(Mathf.Abs(seed) % candidates.Count)].x,
                                  candidates[(Mathf.Abs(seed) % candidates.Count)].y);

        // Prefer cells with more solid neighbors (spawn against a wall)
        int bestScore = int.MinValue;
        CellData best = candidates[0];
        foreach (var c in candidates)
        {
            int score = NeighborSolidCount(c.x, c.y);
            // slight deterministic shuffle by seed
            score = score * 1000 + ((c.x * 73856093) ^ (c.y * 19349663) ^ seed) % 997;
            if (score > bestScore) { bestScore = score; best = c; }
        }
        return new Vector2Int(best.x, best.y);
    }

    int NeighborSolidCount(int x, int y)
    {
        int count = 0;
        if (In(x - 1, y) && IsBlocking(grid.GetCell(x - 1, y).type)) count++;
        if (In(x + 1, y) && IsBlocking(grid.GetCell(x + 1, y).type)) count++;
        if (In(x, y - 1) && IsBlocking(grid.GetCell(x, y - 1).type)) count++;
        if (In(x, y + 1) && IsBlocking(grid.GetCell(x, y + 1).type)) count++;
        return count;
    }

    static bool IsBlocking(CellType type)
        => type == CellType.Solid || type == CellType.EnemySpawner;

    bool In(int x, int y) => grid.InBounds(x, y);
}
