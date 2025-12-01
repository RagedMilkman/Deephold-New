using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DefaultExecutionOrder(ServiceExecutionOrder.BespokePathService)]
public class BespokePathService : MonoBehaviour
{
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

    [SerializeField] GridDirector grid;

    readonly MinHeap frontier = new();
    int[,] gScores;
    int[,] fScores;
    bool[,] closedSet;
    Vector2Int[,] parents;
    bool[,] parentInitialized;

    void Awake()
    {
        if (!grid)
            grid = FindFirstObjectByType<GridDirector>();
    }

    public bool TryFindPath(Vector2Int start, Vector2Int goal, List<Vector2Int> result, IReadOnlyCollection<CellType> traversableCellTypes = null)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        result.Clear();

        if (!ValidateGrid(out var width, out var height))
            return false;

        if (!grid.InBounds(start.x, start.y) || !grid.InBounds(goal.x, goal.y))
            return false;

        if (!IsCellTraversable(start, traversableCellTypes) || !IsCellTraversable(goal, traversableCellTypes))
            return false;

        EnsureBuffers(width, height);
        ResetBuffers(width, height);

        frontier.Clear();

        gScores[start.x, start.y] = 0;
        fScores[start.x, start.y] = Heuristic(start, goal);
        parents[start.x, start.y] = start;
        parentInitialized[start.x, start.y] = true;

        frontier.Enqueue(start, fScores[start.x, start.y]);

        while (frontier.Count > 0)
        {
            var (current, _) = frontier.Dequeue();

            if (closedSet[current.x, current.y])
                continue;

            if (current == goal)
                return ReconstructPath(start, goal, result);

            closedSet[current.x, current.y] = true;

            foreach (var offset in NeighborOffsets)
            {
                var neighbor = new Vector2Int(current.x + offset.x, current.y + offset.y);
                if (!grid.InBounds(neighbor.x, neighbor.y))
                    continue;

                if (closedSet[neighbor.x, neighbor.y])
                    continue;

                if (!TryGetStepCost(current, neighbor, out int stepCost, traversableCellTypes))
                    continue;

                int tentativeG = gScores[current.x, current.y] + stepCost;
                if (tentativeG >= gScores[neighbor.x, neighbor.y])
                    continue;

                gScores[neighbor.x, neighbor.y] = tentativeG;
                parents[neighbor.x, neighbor.y] = current;
                parentInitialized[neighbor.x, neighbor.y] = true;

                int fScore = tentativeG + Heuristic(neighbor, goal);
                fScores[neighbor.x, neighbor.y] = fScore;
                frontier.Enqueue(neighbor, fScore);
            }
        }

        return false;
    }

    bool ValidateGrid(out int width, out int height)
    {
        width = 0;
        height = 0;

        if (!grid)
        {
            grid = FindFirstObjectByType<GridDirector>();
            if (!grid)
                return false;
        }

        width = grid.Width;
        height = grid.Height;
        return width > 0 && height > 0;
    }

    bool TryGetMovementCost(Vector2Int cell, out int cost)
    {
        if (grid.TryGetMovementWeight(cell.x, cell.y, out cost))
            return true;

        cost = int.MaxValue;
        return false;
    }

    bool TryGetStepCost(Vector2Int from, Vector2Int to, out int cost, IReadOnlyCollection<CellType> traversableCellTypes)
    {
        cost = int.MaxValue;

        if (!grid.InBounds(to.x, to.y))
            return false;

        if (!IsCellTraversable(to, traversableCellTypes))
            return false;

        if (!TryGetMovementCost(to, out cost))
            return false;

        if (!IsDiagonal(from, to))
            return true;

        if (!IsCellEmpty(from) || !IsCellEmpty(to))
            return false;

        var horizontal = new Vector2Int(to.x, from.y);
        var vertical = new Vector2Int(from.x, to.y);

        if (!IsCellEmpty(horizontal) || !IsCellEmpty(vertical))
            return false;

        return true;
    }

    bool IsCellTraversable(Vector2Int cell, IReadOnlyCollection<CellType> allowedTypes)
    {
        if (!grid.InBounds(cell.x, cell.y))
            return false;

        var cellData = grid.GetCell(cell.x, cell.y);
        var cellType = cellData.type;

        if (allowedTypes != null)
            return allowedTypes.Contains(cellType);

        return cellData.IsConsideredForPathfinding;
    }

    bool IsCellEmpty(Vector2Int cell)
        => grid.InBounds(cell.x, cell.y) && grid.GetCell(cell.x, cell.y).type == CellType.Empty;

    static bool IsDiagonal(Vector2Int from, Vector2Int to)
        => from.x != to.x && from.y != to.y;

    int Heuristic(Vector2Int from, Vector2Int to)
    {
        int dx = Mathf.Abs(from.x - to.x);
        int dy = Mathf.Abs(from.y - to.y);
        return dx + dy;
    }

    bool ReconstructPath(Vector2Int start, Vector2Int goal, List<Vector2Int> result)
    {
        var current = goal;
        int iterationLimit = grid.Width * grid.Height;

        while (iterationLimit-- > 0)
        {
            result.Add(current);
            if (current == start)
                break;

            if (!parentInitialized[current.x, current.y])
            {
                result.Clear();
                return false;
            }

            current = parents[current.x, current.y];
        }

        if (result.Count == 0 || result[^1] != start)
        {
            result.Clear();
            return false;
        }

        result.Reverse();
        return true;
    }

    void EnsureBuffers(int width, int height)
    {
        gScores ??= new int[width, height];
        fScores ??= new int[width, height];
        closedSet ??= new bool[width, height];
        parents ??= new Vector2Int[width, height];
        parentInitialized ??= new bool[width, height];

        if (gScores.GetLength(0) != width || gScores.GetLength(1) != height)
        {
            gScores = new int[width, height];
            fScores = new int[width, height];
            closedSet = new bool[width, height];
            parents = new Vector2Int[width, height];
            parentInitialized = new bool[width, height];
        }
    }

    void ResetBuffers(int width, int height)
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                gScores[x, y] = int.MaxValue;
                fScores[x, y] = int.MaxValue;
                closedSet[x, y] = false;
                parentInitialized[x, y] = false;
            }
        }
    }

    class MinHeap
    {
        readonly List<(Vector2Int cell, int priority)> heap = new();

        public int Count => heap.Count;

        public void Clear() => heap.Clear();

        public void Enqueue(Vector2Int cell, int priority)
        {
            heap.Add((cell, priority));
            BubbleUp(heap.Count - 1);
        }

        public (Vector2Int cell, int priority) Dequeue()
        {
            if (heap.Count == 0)
                throw new InvalidOperationException("Heap is empty");

            var result = heap[0];
            int lastIndex = heap.Count - 1;
            heap[0] = heap[lastIndex];
            heap.RemoveAt(lastIndex);
            if (heap.Count > 0)
                BubbleDown(0);
            return result;
        }

        void BubbleUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (heap[parent].priority <= heap[index].priority)
                    break;

                (heap[parent], heap[index]) = (heap[index], heap[parent]);
                index = parent;
            }
        }

        void BubbleDown(int index)
        {
            while (true)
            {
                int left = index * 2 + 1;
                int right = index * 2 + 2;
                int smallest = index;

                if (left < heap.Count && heap[left].priority < heap[smallest].priority)
                    smallest = left;
                if (right < heap.Count && heap[right].priority < heap[smallest].priority)
                    smallest = right;

                if (smallest == index)
                    break;

                (heap[smallest], heap[index]) = (heap[index], heap[smallest]);
                index = smallest;
            }
        }
    }
}
