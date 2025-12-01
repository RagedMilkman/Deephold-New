using System;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Connection;

[DefaultExecutionOrder(ServiceExecutionOrder.NavFieldService)]
public class NavFieldService : MonoBehaviour
{
    public const float DebugLineHeight = 0f;

    static readonly Vector2Int[] NeighborOffsets =
    {
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1),
        new Vector2Int(1, 1),
        new Vector2Int(1, -1),
        new Vector2Int(-1, 1),
        new Vector2Int(-1, -1)
    };

    [Serializable]
    public struct FixedDestination
    {
        public string id;
        public Vector2Int cell;
    }

    [SerializeField] GridDirector grid;
    [SerializeField] PlayerLocationService playerLocationService;
    [SerializeField] List<FixedDestination> fixedDestinations = new();
    [SerializeField] bool drawDebugMap;

    INavFieldSolver solver;
    readonly Dictionary<NavDestinationKey, NavFieldRecord> fields = new();
    readonly List<Vector2Int> updateSources = new(1);
    bool initialized;

    void Awake()
    {
        solver ??= new DijkstraNavFieldSolver();

        if (!grid)
            grid = FindFirstObjectByType<GridDirector>();
        if (!playerLocationService)
            playerLocationService = FindFirstObjectByType<PlayerLocationService>();
    }

    void OnEnable()
    {
        if (grid)
            grid.CellsChanged += HandleCellsChanged;
        if (playerLocationService)
            playerLocationService.PlayerCellChanged += HandlePlayerCellChanged;
    }

    void OnDisable()
    {
        if (grid)
            grid.CellsChanged -= HandleCellsChanged;
        if (playerLocationService)
            playerLocationService.PlayerCellChanged -= HandlePlayerCellChanged;
    }

    void Start()
    {
        Initialize();
    }

    void Update()
    {
        if (drawDebugMap)
        {
            DrawDebugNavFields();
        }
    }

    public void SetSolver(INavFieldSolver newSolver, bool rebuildExisting = true)
    {
        solver = newSolver ?? new DijkstraNavFieldSolver();
        if (rebuildExisting && grid)
        {
            foreach (var record in fields.Values)
            {
                EnsureDistanceArray(record);
                solver.ComputeFull(grid, record.DestinationCell, record.Distances);
            }
        }
    }

    public void RegisterFixedDestination(string id, Vector2Int cell)
    {
        if (string.IsNullOrEmpty(id) || grid == null)
            return;
        if (!grid.InBounds(cell.x, cell.y))
            return;

        var key = NavDestinationKey.ForFixed(id);
        var record = GetOrCreateRecord(key);
        record.DestinationCell = cell;
        EnsureDistanceArray(record);
        solver.ComputeFull(grid, cell, record.Distances);
    }

    public bool TryGetPath(NavDestinationKey destination, Vector2Int start, List<Vector2Int> result)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        result.Clear();

        if (grid == null || !fields.TryGetValue(destination, out var record))
            return false;
        if (record.Distances == null || !grid.InBounds(start.x, start.y))
            return false;

        int currentCost = record.Distances[start.x, start.y];
        if (currentCost == int.MaxValue)
            return false;

        var current = start;
        result.Add(current);

        int iterationLimit = grid.Width * grid.Height;
        while (current != record.DestinationCell && iterationLimit-- > 0)
        {
            if (!TryGetNextStep(record, current, out var next, out var nextCost))
                return false;

            if (nextCost >= currentCost && next != record.DestinationCell)
                return false;

            current = next;
            currentCost = nextCost;
            result.Add(current);
        }

        return current == record.DestinationCell;
    }

    public bool HasDestination(NavDestinationKey destination)
        => fields.ContainsKey(destination);

    public void RemoveFixedDestination(string id)
    {
        if (string.IsNullOrEmpty(id))
            return;

        var key = NavDestinationKey.ForFixed(id);
        fields.Remove(key);
    }

    void Initialize()
    {
        if (initialized)
            return;

        initialized = true;

        if (!grid)
            grid = FindFirstObjectByType<GridDirector>();
        if (!playerLocationService)
            playerLocationService = FindFirstObjectByType<PlayerLocationService>();

        if (!grid)
            return;

        foreach (var destination in fixedDestinations)
        {
            RegisterFixedDestination(destination.id, destination.cell);
        }

        if (playerLocationService != null)
        {
            foreach (var kv in playerLocationService.KnownPlayers)
            {
                RegisterPlayerDestination(kv.Key, kv.Value);
            }
        }
    }

    void HandleCellsChanged(IReadOnlyList<Vector2Int> cells)
    {
        if (grid == null || fields.Count == 0 || cells == null || cells.Count == 0)
            return;

        updateSources.Clear();
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (!grid.InBounds(cell.x, cell.y))
                continue;

            updateSources.Add(cell);
        }

        if (updateSources.Count == 0)
            return;

        foreach (var record in fields.Values)
        {
            if (record.Distances == null)
                continue;

            solver.UpdateFromSources(grid, record.DestinationCell, record.Distances, updateSources);
        }
    }

    void HandlePlayerCellChanged(NetworkConnection player, Vector2Int? previous, Vector2Int? next)
    {
        var key = NavDestinationKey.ForPlayer(player);

        if (next.HasValue)
        {
            if (grid == null)
                return;

            var cell = next.Value;
            if (!grid.InBounds(cell.x, cell.y))
                return;

            RegisterPlayerDestination(player, cell);
        }
        else
        {
            fields.Remove(key);
        }
    }

    void RegisterPlayerDestination(NetworkConnection player, Vector2Int cell)
    {
        if (grid == null)
            return;

        var key = NavDestinationKey.ForPlayer(player);
        var record = GetOrCreateRecord(key);
        record.DestinationCell = cell;
        EnsureDistanceArray(record);
        solver.ComputeFull(grid, cell, record.Distances);
    }

    bool TryGetNextStep(NavFieldRecord record, Vector2Int current, out Vector2Int next, out int nextCost)
    {
        next = current;
        nextCost = record.Distances[current.x, current.y];
        bool found = false;

        foreach (var offset in NeighborOffsets)
        {
            int nx = current.x + offset.x;
            int ny = current.y + offset.y;

            if (!grid.InBounds(nx, ny))
                continue;

            if (!IsStepAllowed(current, offset))
                continue;

            int candidateCost = record.Distances[nx, ny];
            if (candidateCost >= nextCost)
                continue;

            next = new Vector2Int(nx, ny);
            nextCost = candidateCost;
            found = true;
        }

        return found;
    }

    bool IsStepAllowed(Vector2Int current, Vector2Int offset)
    {
        if (offset.x == 0 || offset.y == 0)
            return true;

        if (!IsCellEmpty(current))
            return false;

        var next = new Vector2Int(current.x + offset.x, current.y + offset.y);
        if (!IsCellEmpty(next))
            return false;

        var horizontal = new Vector2Int(next.x, current.y);
        var vertical = new Vector2Int(current.x, next.y);

        if (!grid.InBounds(horizontal.x, horizontal.y) || !grid.InBounds(vertical.x, vertical.y))
            return false;

        return IsCellEmpty(horizontal) && IsCellEmpty(vertical);
    }

    bool IsCellEmpty(Vector2Int cell)
        => grid.InBounds(cell.x, cell.y) && grid.GetCell(cell.x, cell.y).type == CellType.Empty;

    NavFieldRecord GetOrCreateRecord(NavDestinationKey key)
    {
        if (!fields.TryGetValue(key, out var record))
        {
            record = new NavFieldRecord(key, grid.Width, grid.Height);
            fields.Add(key, record);
        }
        else
        {
            record.EnsureSize(grid.Width, grid.Height);
        }

        return record;
    }

    void EnsureDistanceArray(NavFieldRecord record)
    {
        if (record.Distances == null || record.Distances.GetLength(0) != grid.Width || record.Distances.GetLength(1) != grid.Height)
            record.Distances = new int[grid.Width, grid.Height];
    }

    void DrawDebugNavFields()
    {
        if (!grid || fields.Count == 0)
            return;

        foreach (var record in fields.Values)
        {
            var distances = record.Distances;
            if (distances == null ||
                distances.GetLength(0) != grid.Width ||
                distances.GetLength(1) != grid.Height)
            {
                continue;
            }

            Color color = GetDebugColor(record.Key);

            for (int x = 0; x < grid.Width; x++)
            {
                for (int y = 0; y < grid.Height; y++)
                {
                    if (distances[x, y] == int.MaxValue)
                        continue;

                    var cell = new Vector2Int(x, y);
                    if (!TryGetNextStep(record, cell, out var next, out _))
                        continue;

                    Vector3 start = grid.CellToWorldCenter(cell.x, cell.y);
                    start.y = DebugLineHeight;

                    Vector3 end = grid.CellToWorldCenter(next.x, next.y);
                    end.y = DebugLineHeight;

                    Vector3 direction = end - start;
                    float length = direction.magnitude;
                    if (length <= Mathf.Epsilon)
                        continue;

                    Vector3 mid = start + direction * 0.5f;
                    Vector3 normalized = direction / length;

                    Color sourceColor = Color.Lerp(color, Color.black, 0.35f);
                    Color destinationColor = color;

                    Debug.DrawLine(start, mid, sourceColor);
                    Debug.DrawLine(mid, end, destinationColor);

                    float arrowLength = Mathf.Min(grid.CellSize * 0.35f, length * 0.5f);
                    if (arrowLength > 0f)
                    {
                        Vector3 arrowBase = end - normalized * arrowLength;
                        Vector3 perpendicular = Vector3.Cross(normalized, Vector3.up);
                        if (perpendicular.sqrMagnitude <= Mathf.Epsilon)
                        {
                            perpendicular = Vector3.Cross(normalized, Vector3.right);
                        }
                        perpendicular = perpendicular.normalized * arrowLength * 0.5f;

                        Debug.DrawLine(end, arrowBase + perpendicular, destinationColor);
                        Debug.DrawLine(end, arrowBase - perpendicular, destinationColor);
                    }
                }
            }
        }
    }

    static Color GetDebugColor(NavDestinationKey key)
    {
        uint hash = unchecked((uint)key.GetHashCode());
        float hue = (hash % 360u) / 360f;
        float saturation = 0.85f;
        float value = 1f;
        return Color.HSVToRGB(hue, saturation, value);
    }

    class NavFieldRecord
    {
        public NavDestinationKey Key { get; }
        public Vector2Int DestinationCell;
        public int[,] Distances;

        public NavFieldRecord(NavDestinationKey key, int width, int height)
        {
            Key = key;
            Distances = new int[width, height];
        }

        public void EnsureSize(int width, int height)
        {
            if (Distances == null || Distances.GetLength(0) != width || Distances.GetLength(1) != height)
                Distances = new int[width, height];
        }
    }
}

public enum NavDestinationType
{
    Fixed,
    Player
}

public readonly struct NavDestinationKey : IEquatable<NavDestinationKey>
{
    public NavDestinationType Type { get; }
    public string FixedId { get; }
    public NetworkConnection PlayerId { get; }

    NavDestinationKey(NavDestinationType type, string fixedId, NetworkConnection playerId)
    {
        Type = type;
        FixedId = fixedId;
        PlayerId = playerId;
    }

    public static NavDestinationKey ForFixed(string id)
        => new(NavDestinationType.Fixed, id ?? string.Empty, default);

    public static NavDestinationKey ForPlayer(NetworkConnection id)
        => new(NavDestinationType.Player, string.Empty, id);

    public bool Equals(NavDestinationKey other)
    {
        if (Type != other.Type)
            return false;

        return Type == NavDestinationType.Fixed
            ? string.Equals(FixedId, other.FixedId, StringComparison.Ordinal)
            : PlayerId.Equals(other.PlayerId);
    }

    public override bool Equals(object obj)
        => obj is NavDestinationKey other && Equals(other);

    public override int GetHashCode()
    {
        return Type == NavDestinationType.Fixed
            ? HashCode.Combine(Type, StringComparer.Ordinal.GetHashCode(FixedId ?? string.Empty))
            : HashCode.Combine(Type, PlayerId);
    }

    public override string ToString()
        => Type == NavDestinationType.Fixed ? $"Fixed:{FixedId}" : $"Player:{PlayerId}";
}
