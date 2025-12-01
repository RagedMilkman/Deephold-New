using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(ServiceExecutionOrder.PathingService)]
public class PathingService : MonoBehaviour
{
    [SerializeField] NavFieldService navFieldService;
    [SerializeField] BespokePathService bespokePathService;
    [SerializeField] bool useNavFieldService = true;

    public bool UseNavFieldService
    {
        get => useNavFieldService;
        set => useNavFieldService = value;
    }

    void Awake()
    {
        EnsureDependencies();
    }

    public bool TryGetPath(PathRequest request, List<Vector2Int> result)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        result.Clear();

        EnsureDependencies();

        if (TryGetNavFieldPath(request, result))
            return true;

        if (bespokePathService && bespokePathService.TryFindPath(request.Start, request.Destination, result, request.TraversableCellTypes))
            return true;

        result.Clear();
        return false;
    }

    public bool TryGetPath(Vector2Int start, Vector2Int destination, List<Vector2Int> result)
        => TryGetPath(new PathRequest(start, destination), result);

    public bool TryGetPath(Vector2Int start, Vector2Int destination, NavDestinationKey destinationKey, List<Vector2Int> result)
        => TryGetPath(new PathRequest(start, destination, destinationKey), result);

    public bool TryGetPath(Vector2Int start, Vector2Int destination, NavDestinationKey? destinationKey, IReadOnlyCollection<CellType> traversableCellTypes, List<Vector2Int> result)
        => TryGetPath(new PathRequest(start, destination, destinationKey, traversableCellTypes), result);

    bool TryGetNavFieldPath(PathRequest request, List<Vector2Int> result)
    {
        if (request.TraversableCellTypes != null)
            return false;

        if (!useNavFieldService || !request.DestinationKey.HasValue || !navFieldService)
            return false;

        var key = request.DestinationKey.Value;
        if (!navFieldService.HasDestination(key))
            return false;

        if (!navFieldService.TryGetPath(key, request.Start, result))
        {
            result.Clear();
            return false;
        }

        if (result.Count == 0)
            return false;

        if (result[^1] != request.Destination)
        {
            result.Clear();
            return false;
        }

        return true;
    }

    void EnsureDependencies()
    {
        if (useNavFieldService)
        {
            if (!navFieldService)
                navFieldService = FindFirstObjectByType<NavFieldService>();
        }
        else
        {
            navFieldService = null;
        }
        if (!bespokePathService)
            bespokePathService = FindFirstObjectByType<BespokePathService>();
    }

    public readonly struct PathRequest
    {
        public Vector2Int Start { get; }
        public Vector2Int Destination { get; }
        public NavDestinationKey? DestinationKey { get; }
        public IReadOnlyCollection<CellType> TraversableCellTypes { get; }

        public PathRequest(Vector2Int start, Vector2Int destination, NavDestinationKey? destinationKey = null, IReadOnlyCollection<CellType> traversableCellTypes = null)
        {
            Start = start;
            Destination = destination;
            DestinationKey = destinationKey;
            TraversableCellTypes = traversableCellTypes;
        }
    }
}
