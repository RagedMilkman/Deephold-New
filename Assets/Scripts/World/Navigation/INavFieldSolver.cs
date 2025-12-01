using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Solver responsible for generating and updating navigation fields.
/// </summary>
public interface INavFieldSolver
{
    /// <summary>
    /// Computes the full navigation field for the given destination.
    /// Existing values in <paramref name="distances"/> will be replaced.
    /// </summary>
    /// <param name="grid">Grid meta data provider.</param>
    /// <param name="destination">Destination cell.</param>
    /// <param name="distances">Distance field to populate. Must match the grid dimensions.</param>
    void ComputeFull(GridDirector grid, Vector2Int destination, int[,] distances);

    /// <summary>
    /// Recalculates the navigation field starting from the supplied sources.
    /// Only cells affected by the change will be touched.
    /// </summary>
    /// <param name="grid">Grid meta data provider.</param>
    /// <param name="destination">Destination cell.</param>
    /// <param name="distances">Existing distance field to mutate.</param>
    /// <param name="sources">Cells that should be reconsidered.</param>
    void UpdateFromSources(GridDirector grid, Vector2Int destination, int[,] distances, IReadOnlyCollection<Vector2Int> sources);
}
