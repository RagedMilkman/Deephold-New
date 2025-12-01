using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dijkstra based implementation of <see cref="INavFieldSolver"/>.
/// </summary>
public class DijkstraNavFieldSolver : INavFieldSolver
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

    readonly MinHeap frontier = new();
    readonly HashSet<Vector2Int> visited = new();

    public void ComputeFull(GridDirector grid, Vector2Int destination, int[,] distances)
    {
        if (!ValidateInputs(grid, distances))
            return;

        ResetDistances(distances, int.MaxValue);
        frontier.Clear();
        visited.Clear();

        if (!grid.InBounds(destination.x, destination.y))
            return;

        distances[destination.x, destination.y] = 0;
        frontier.Enqueue(destination, 0);

        while (frontier.Count > 0)
        {
            var (cell, cost) = frontier.Dequeue();
            if (!grid.InBounds(cell.x, cell.y))
                continue;

            if (cost != distances[cell.x, cell.y])
                continue;

            foreach (var offset in NeighborOffsets)
            {
                int nx = cell.x + offset.x;
                int ny = cell.y + offset.y;

                if (!grid.InBounds(nx, ny))
                    continue;

                var neighbor = new Vector2Int(nx, ny);
                if (!TryGetStepCost(grid, cell, neighbor, out int stepCost, out bool targetBlocked))
                {
                    if (targetBlocked && distances[nx, ny] != int.MaxValue)
                        distances[nx, ny] = int.MaxValue;
                    continue;
                }

                int newCost = cost + stepCost;
                if (newCost < distances[nx, ny])
                {
                    distances[nx, ny] = newCost;
                    frontier.Enqueue(neighbor, newCost);
                }
            }
        }
    }

    public void UpdateFromSources(GridDirector grid, Vector2Int destination, int[,] distances, IReadOnlyCollection<Vector2Int> sources)
    {
        if (!ValidateInputs(grid, distances) || sources == null || sources.Count == 0)
            return;

        frontier.Clear();
        visited.Clear();

        foreach (var source in sources)
        {
            if (!grid.InBounds(source.x, source.y))
                continue;

            int recalculated = ComputeBestDistance(grid, destination, distances, source);
            if (recalculated != distances[source.x, source.y])
            {
                distances[source.x, source.y] = recalculated;
                frontier.Enqueue(source, SafeCost(recalculated));
            }
        }

        while (frontier.Count > 0)
        {
            var (cell, cost) = frontier.Dequeue();
            if (visited.Contains(cell))
                continue;

            visited.Add(cell);

            foreach (var offset in NeighborOffsets)
            {
                int nx = cell.x + offset.x;
                int ny = cell.y + offset.y;

                if (!grid.InBounds(nx, ny))
                    continue;

                var neighbor = new Vector2Int(nx, ny);
                int recalculated = ComputeBestDistance(grid, destination, distances, neighbor);
                if (recalculated != distances[nx, ny])
                {
                    distances[nx, ny] = recalculated;
                    frontier.Enqueue(neighbor, SafeCost(recalculated));
                }
            }
        }
    }

    static bool ValidateInputs(GridDirector grid, int[,] distances)
        => grid != null && distances != null &&
           distances.GetLength(0) == grid.Width && distances.GetLength(1) == grid.Height;

    static void ResetDistances(int[,] distances, int value)
    {
        int width = distances.GetLength(0);
        int height = distances.GetLength(1);
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                distances[x, y] = value;
    }

    static bool TryGetMovementCost(GridDirector grid, Vector2Int cell, out int cost)
    {
        if (!grid.TryGetMovementWeight(cell.x, cell.y, out cost))
        {
            cost = int.MaxValue;
            return false;
        }

        return true;
    }

    static int ComputeBestDistance(GridDirector grid, Vector2Int destination, int[,] distances, Vector2Int cell)
    {
        if (cell == destination)
            return 0;

        if (!TryGetMovementCost(grid, cell, out int ownCost))
            return int.MaxValue;

        int best = int.MaxValue;
        foreach (var offset in NeighborOffsets)
        {
            int nx = cell.x + offset.x;
            int ny = cell.y + offset.y;

            if (!grid.InBounds(nx, ny))
                continue;

            var neighbor = new Vector2Int(nx, ny);
            if (!TryGetStepCost(grid, cell, neighbor, out _, out _))
                continue;

            int neighborCost = distances[nx, ny];
            if (neighborCost == int.MaxValue)
                continue;

            int candidate = neighborCost + ownCost;
            if (candidate < best)
                best = candidate;
        }

        return best;
    }

    static bool TryGetStepCost(GridDirector grid, Vector2Int from, Vector2Int to, out int cost, out bool targetBlocked)
    {
        cost = int.MaxValue;
        targetBlocked = false;

        if (!grid.InBounds(to.x, to.y))
            return false;

        if (!TryGetMovementCost(grid, to, out cost))
        {
            targetBlocked = true;
            return false;
        }

        if (!IsDiagonal(from, to))
            return true;

        if (!IsCellEmpty(grid, from) || !IsCellEmpty(grid, to))
            return false;

        var horizontal = new Vector2Int(to.x, from.y);
        var vertical = new Vector2Int(from.x, to.y);

        if (!grid.InBounds(horizontal.x, horizontal.y) || !grid.InBounds(vertical.x, vertical.y))
            return false;

        if (!IsCellEmpty(grid, horizontal) || !IsCellEmpty(grid, vertical))
            return false;

        return true;
    }

    static bool IsDiagonal(Vector2Int from, Vector2Int to)
        => from.x != to.x && from.y != to.y;

    static bool IsCellEmpty(GridDirector grid, Vector2Int cell)
        => grid.InBounds(cell.x, cell.y) && grid.GetCell(cell.x, cell.y).type == CellType.Empty;

    static int SafeCost(int cost) => cost == int.MaxValue ? int.MaxValue : cost;

    /// <summary>
    /// Simple binary heap implementation for the solver to avoid dependency on newer frameworks.
    /// </summary>
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
