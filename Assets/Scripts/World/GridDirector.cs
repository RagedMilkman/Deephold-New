using System;
using System.Collections.Generic;
using UnityEngine;
using System.Collections;
using System.Linq;
using FishNet.Object;
using FishNet.Connection;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DefaultExecutionOrder(ServiceExecutionOrder.GridDirector)]
public class GridDirector : NetworkBehaviour
{
    [Header("Grid meta")]
    [SerializeField] int width = 64;
    [SerializeField] int height = 64;
    [SerializeField] float cellSize = 1f;

    [Header("Services")]
    [SerializeField] EnemySpawnLocationService enemySpawnLocationService;
    [SerializeField] NoiseGenerationService noiseGenerationService;

    [Header("Init")]
    [SerializeField] bool holeInCenter = true;
    [SerializeField] int centerHoleSize = 10;
    [SerializeField] int defaultHp = 3;

    [Header("Debug")]
    [SerializeField] bool drawMovementWeights;

    [Header("Server Physics")]
    [SerializeField] bool buildServerColliders = true;
    [SerializeField] float serverColliderHeight = 1f;
    [SerializeField] Transform serverColliderRoot;
    [SerializeField] int serverColliderLayer = 3;

    // Authoritative state
    private CellData[,] cells;

    // Server-side physics representation (dedicated server pathing)
    readonly Dictionary<Vector2Int, BoxCollider> serverColliders = new();

    public event Action<IReadOnlyList<Vector2Int>> CellsChanged;

    public int Width => width;
    public int Height => height;
    public float CellSize => cellSize;

#if UNITY_EDITOR
    GUIStyle debugWeightLabelStyle;
#endif

    // -------------------- Lifecycle --------------------

    public override void OnStartServer()
    {
        if (!IsServer)
            return;

        BuildInitialGrid();

        var noiseService = noiseGenerationService ? noiseGenerationService : FindFirstObjectByType<NoiseGenerationService>();
        if (noiseService && !noiseGenerationService)
            noiseGenerationService = noiseService;
        noiseService?.ApplyNoiseMap(this);

        var spawnerService = enemySpawnLocationService ? enemySpawnLocationService : FindFirstObjectByType<EnemySpawnLocationService>();
        if (spawnerService && !enemySpawnLocationService)
            enemySpawnLocationService = spawnerService;
        spawnerService?.ServerInitializeSpawners(this);

        if (ServerCollidersActive)
            RebuildServerColliders();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        Server_SendFullSnapshotToClient();
    }

    void BuildInitialGrid()
    {
        cells = new CellData[width, height];

        // Fill with diggable
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                cells[x, y] = CreateCell(x, y, CellType.Diggable, (short)defaultHp);

        // Border: Solid
        for (int x = 0; x < width; x++)
        { cells[x, 0].type = CellType.Solid; cells[x, height - 1].type = CellType.Solid; }
        for (int y = 0; y < height; y++)
        { cells[0, y].type = CellType.Solid; cells[width - 1, y].type = CellType.Solid; }

        // Central hole
        if (holeInCenter)
        {
            int w = Mathf.Clamp(centerHoleSize, 0, width);
            int h = Mathf.Clamp(centerHoleSize, 0, height);
            int x0 = (width - w) / 2;
            int y0 = (height - h) / 2;

            for (int x = x0; x < x0 + w; x++)
                for (int y = y0; y < y0 + h; y++)
                    cells[x, y] = CreateCell(x, y, CellType.Empty, 0);
        }
    }

    CellData CreateCell(int x, int y, CellType type, short hp)
    {
        return new CellData
        {
            x = x,
            y = y,
            type = type,
            hp = hp,
            players = new List<NetworkConnection>()
        };
    }

    static CellData WithoutPlayers(CellData cell)
    {
        cell.players = null;
        return cell;
    }

    // -------------------- Authority ops --------------------

    /// Apply a mining hit at world position (called by MineableBlock server RPC).
    public void ServerApplyHitAtWorld(Vector3 worldPos, int amount)
    {
        if (!IsServer || amount <= 0) return;
        if (!TryWorldToCell(worldPos, out int x, out int y)) return;

        ref var c = ref cells[x, y];
        if (c.type != CellType.Diggable) return;

        int newHp = Mathf.Max(0, c.hp - amount);
        c.hp = (short)newHp;

        if (newHp == 0)
        {
            c.type = CellType.Empty;
            c.ClearMovementWeightOverride();
            // Send delta (just this cell) to all clients
            Broadcast_Client_UpdateBlocks(new[] { WithoutPlayers(c) });
            NotifyCellChanged(x, y);
            UpdateServerCollider(new Vector2Int(x, y));
        }
        else
        {
            // Optional VFX stream: Broadcast hp-only deltas if you want
            // Broadcast_Client_UpdateBlocks(new [] { c });
        }
    }

    // -------------------- Queries (read-only) --------------------

    public bool TryWorldToCell(Vector3 world, out int x, out int y)
    {
        x = Mathf.FloorToInt(world.x / cellSize);
        y = Mathf.FloorToInt(world.z / cellSize);
        return (uint)x < (uint)width && (uint)y < (uint)height;
    }

    public Vector3 CellToWorldCenter(int x, int y)
      => new Vector3((x + 0.5f) * cellSize, 0f, (y + 0.5f) * cellSize);

    public bool InBounds(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    public CellData GetCell(int x, int y) => cells[x, y];

    public bool TryGetMovementWeight(int x, int y, out int weight)
    {
        weight = int.MaxValue;

        if (cells == null || !InBounds(x, y))
            return false;

        var cell = cells[x, y];
        var movementWeight = cell.MovementWeight;
        if (!movementWeight.HasValue)
            return false;

        weight = movementWeight.Value;
        return true;
    }

    public IReadOnlyList<NetworkConnection> GetPlayersInCell(int x, int y)
    {
        if (!InBounds(x, y))
            return Array.Empty<NetworkConnection>();

        var list = cells[x, y].players;
        return list ?? Array.Empty<NetworkConnection>().ToList();
    }

    public void ServerAssignPlayerToCell(NetworkConnection player, Vector2Int cell)
    {
        if (cells == null)
            return;

        if (!InBounds(cell.x, cell.y))
            return;

        ref var data = ref cells[cell.x, cell.y];
        data.players ??= new List<NetworkConnection>();
        if (!data.players.Contains(player))
            data.players.Add(player);
    }

    public void ServerRemovePlayerFromCell(NetworkConnection player, Vector2Int cell)
    {
        if (cells == null)
            return;

        if (!InBounds(cell.x, cell.y))
            return;

        ref var data = ref cells[cell.x, cell.y];
        data.players?.Remove(player);
    }

    /// Returns a copy array of the full grid (for client init).
    public CellData[] GetFullSnapshot()
    {
        var flat = new CellData[width * height];
        int i = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                flat[i++] = WithoutPlayers(cells[x, y]);
        return flat;
    }

    /// Returns all cells in a RectInt region (clamped to bounds).
    public List<CellData> GetRegion(RectInt region)
    {
        var outList = new List<CellData>(region.width * region.height);
        int x0 = Mathf.Clamp(region.xMin, 0, width - 1);
        int y0 = Mathf.Clamp(region.yMin, 0, height - 1);
        int x1 = Mathf.Clamp(region.xMax, 0, width - 1);
        int y1 = Mathf.Clamp(region.yMax, 0, height - 1);

        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                outList.Add(WithoutPlayers(cells[x, y]));
        return outList;
    }

    /// Enumerate cells that satisfy a predicate in a region.
    public List<CellData> GetRegionWhere(RectInt region, Func<CellData, bool> predicate)
    {
        var all = GetRegion(region);
        if (predicate == null) return all;
        var filtered = new List<CellData>(all.Count);
        foreach (var c in all) if (predicate(c)) filtered.Add(c);
        return filtered;
    }

    /// Convenience: empty cells in region (good for spawning).
    public List<CellData> GetEmptyInRegion(RectInt region)
      => GetRegionWhere(region, c => c.type == CellType.Empty);

    // -------------------- Client fanout (wire to your netcode) --------------------

    public void ServerRegisterEnemySpawners(IReadOnlyList<Vector2Int> positions)
    {
        if (!IsServer || positions == null || cells == null) return;

        var deltas = new List<CellData>();
        var changedCells = new List<Vector2Int>();

        foreach (var pos in positions)
        {
            if (!InBounds(pos.x, pos.y))
                continue;

            ref var cell = ref cells[pos.x, pos.y];
            if (cell.type == CellType.EnemySpawner)
                continue;

            cell.type = CellType.EnemySpawner;
            cell.hp = 0;
            cell.ClearMovementWeightOverride();
            deltas.Add(cell);

            changedCells.Add(new Vector2Int(pos.x, pos.y));
            ClearCellsAround(pos, deltas, changedCells);
        }

        if (deltas.Count > 0)
        {
            var sanitized = new CellData[deltas.Count];
            for (int i = 0; i < deltas.Count; i++)
                sanitized[i] = WithoutPlayers(deltas[i]);
            Broadcast_Client_UpdateBlocks(sanitized);
            NotifyCellsChanged(changedCells);
            if (ServerCollidersActive)
                UpdateServerColliders(changedCells);
        }
    }

    void ClearCellsAround(Vector2Int center, List<CellData> deltas, List<Vector2Int> changedCells)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                int nx = center.x + dx;
                int ny = center.y + dy;

                if (!InBounds(nx, ny))
                    continue;

                ref var neighbor = ref cells[nx, ny];
                if (neighbor.type != CellType.Diggable)
                    continue;

                neighbor.type = CellType.Empty;
                neighbor.hp = 0;
                neighbor.ClearMovementWeightOverride();
                deltas.Add(neighbor);
                changedCells.Add(new Vector2Int(nx, ny));
            }
        }
    }

    public void ServerApplyMovementWeights(byte[,] weights)
    {
        if (!IsServer || cells == null || weights == null)
            return;

        int gridWidth = cells.GetLength(0);
        int gridHeight = cells.GetLength(1);

        if (weights.GetLength(0) != gridWidth || weights.GetLength(1) != gridHeight)
        {
            Debug.LogWarning("Noise map dimensions do not match grid dimensions.");
            return;
        }

        var deltas = new List<CellData>();
        var changedCells = new List<Vector2Int>();

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                ref var cell = ref cells[x, y];
                if (cell.type != CellType.Diggable)
                    continue;

                var weight = weights[x, y];
                if (!cell.ApplyMovementWeightOverride(weight))
                    continue;

                deltas.Add(cell);
                changedCells.Add(new Vector2Int(x, y));
            }
        }

        if (deltas.Count > 0)
        {
            var sanitized = new CellData[deltas.Count];
            for (int i = 0; i < deltas.Count; i++)
                sanitized[i] = WithoutPlayers(deltas[i]);
            Broadcast_Client_UpdateBlocks(sanitized);
            NotifyCellsChanged(changedCells);
        }
    }

    void NotifyCellChanged(int x, int y)
    {
        if (!InBounds(x, y))
            return;

        NotifyCellsChanged(new[] { new Vector2Int(x, y) });
    }

    void NotifyCellsChanged(IReadOnlyList<Vector2Int> cells)
    {
        if (cells == null || cells.Count == 0)
            return;

        CellsChanged?.Invoke(cells);
    }

    bool ServerCollidersActive
        => buildServerColliders && IsServer && (!IsClient || Application.isBatchMode);

    void RebuildServerColliders()
    {
        ClearServerColliders();
        if (cells == null)
            return;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                UpdateServerCollider(new Vector2Int(x, y));
            }
        }
    }

    void UpdateServerColliders(IReadOnlyList<Vector2Int> coords)
    {
        if (!ServerCollidersActive || coords == null)
            return;

        for (int i = 0; i < coords.Count; i++)
            UpdateServerCollider(coords[i]);
    }

    void UpdateServerCollider(Vector2Int coord)
    {
       // if (!ServerCollidersActive)
       //     return;

        if (cells == null || !InBounds(coord.x, coord.y))
        {
            RemoveServerCollider(coord);
            return;
        }

        ref var data = ref cells[coord.x, coord.y];
        if (!IsBlockingCell(data.type))
        {
            RemoveServerCollider(coord);
            return;
        }

        if (!serverColliders.TryGetValue(coord, out var collider) || collider == null)
        {
            var go = new GameObject($"ServerBlock_{coord.x}_{coord.y}");
            go.transform.SetParent(serverColliderRoot ? serverColliderRoot : transform, false);
            go.layer = serverColliderLayer >= 0 ? serverColliderLayer : 0;
            collider = go.AddComponent<BoxCollider>();
            serverColliders[coord] = collider;
        }

        collider.size = new Vector3(cellSize, serverColliderHeight, cellSize);
        collider.center = Vector3.zero;

        var t = collider.transform;
        Vector3 center = CellToWorldCenter(coord.x, coord.y);
        t.position = new Vector3(center.x, serverColliderHeight * 0.5f, center.z);
    }

    void RemoveServerCollider(Vector2Int coord)
    {
        if (!serverColliders.TryGetValue(coord, out var collider))
            return;

        if (collider)
            Destroy(collider.gameObject);

        serverColliders.Remove(coord);
    }

    void ClearServerColliders()
    {
        if (serverColliders.Count == 0)
            return;

        foreach (var kvp in serverColliders)
        {
            if (kvp.Value)
                Destroy(kvp.Value.gameObject);
        }
        serverColliders.Clear();
    }

    static bool IsBlockingCell(CellType type)
        => type == CellType.Diggable || type == CellType.Solid || type == CellType.EnemySpawner;

    void OnDestroy()
    {
        ClearServerColliders();
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!drawMovementWeights)
            return;

        if (!Application.isPlaying || cells == null)
            return;

        debugWeightLabelStyle ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleCenter
        };

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var cell = cells[x, y];
                var movementWeight = cell.MovementWeight;
                if (!movementWeight.HasValue)
                    continue;

                var position = CellToWorldCenter(x, y);
                position.y += 0.05f;

                var color = cell.hasMovementWeightOverride ? Color.yellow : Color.white;
                debugWeightLabelStyle.normal.textColor = color;
                Handles.Label(position, movementWeight.Value.ToString(), debugWeightLabelStyle);
            }
        }
    }
#endif

    // Called by the client that needs the snapshot.
    [ServerRpc(RequireOwnership = false)]
    private void Server_SendFullSnapshotToClient(NetworkConnection sender = null)
    {
        // Safety check – this will only actually run on the server
        if (!IsServer)
            return;

        if (sender == null)
            return; // should not happen, but keeps us safe

        // Send to that specific client only
        Target_SetWorldMeta(sender, width, height, cellSize);
        Target_InitBlocks(sender, GetFullSnapshot());
    }

    // TargetRpc: first parameter MUST be NetworkConnection.
    [TargetRpc]
    private void Target_SetWorldMeta(NetworkConnection conn, int w, int h, float size)
    {
        foreach (var dir in FindObjectsOfType<WorldGenerationDirector>())
            dir.SetWorldMeta(w, h, size);
    }

    [TargetRpc]
    private void Target_InitBlocks(NetworkConnection conn, CellData[] snapshot)
    {
        foreach (var dir in FindObjectsOfType<WorldGenerationDirector>())
            dir.InitBlocks(snapshot);
    }

    [ObserversRpc]
    void Broadcast_Client_UpdateBlocks(CellData[] deltas)
    {
        foreach (var dir in FindObjectsOfType<WorldGenerationDirector>())
            dir.UpdateBlocks(deltas);
    }
}
