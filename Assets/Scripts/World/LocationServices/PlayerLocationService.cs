using System.Collections.Generic;
using UnityEngine;
using System;
using FishNet.Object;
using FishNet.Connection;

/// <summary>
/// Tracks which grid cell each player currently occupies.
/// Keeps GridDirector player lists synchronised and exposes lookup helpers.
/// </summary>
[DefaultExecutionOrder(ServiceExecutionOrder.PlayerLocation)]
public class PlayerLocationService : NetworkBehaviour
{
    [SerializeField] private GridDirector grid;

    readonly Dictionary<NetworkConnection, Vector2Int> playerCells = new();
    readonly HashSet<NetworkConnection> observedThisFrame = new();
    readonly List<NetworkConnection> toRemove = new();

    public event Action<NetworkConnection, Vector2Int?, Vector2Int?> PlayerCellChanged;

    public IEnumerable<KeyValuePair<NetworkConnection, Vector2Int>> KnownPlayers => playerCells;

    void Awake()
    {
        if (!grid)
            grid = FindFirstObjectByType<GridDirector>();
    }

    void Update()
    {
        if (!IsServer || !grid)
            return;

        observedThisFrame.Clear();

        var players = FindObjectsByType<PlayerData>(FindObjectsSortMode.None);
        foreach (var player in players)
        {

            if (!player || !player.TryGetComponent(out NetworkObject identity))
                continue;

            var trackedTransform = ResolveCharacterTransform(player, out var state);
            if (!trackedTransform)
                continue;

            var playerId = identity.Owner;
            observedThisFrame.Add(playerId);

            if (state && state.State == LifeState.Dead)
            {
                RemovePlayer(playerId);
                continue;
            }

            if (!grid.TryWorldToCell(trackedTransform.position, out int cx, out int cy))
            {
                RemovePlayer(playerId);
                continue;
            }

            var cell = new Vector2Int(cx, cy);
            Vector2Int? previous = null;
            bool changed = false;
            if (playerCells.TryGetValue(playerId, out var current))
            {
                if (current == cell)
                {
                    EnsurePlayerRecorded(playerId, cell);
                    continue;
                }

                previous = current;
                grid.ServerRemovePlayerFromCell(playerId, current);
                changed = true;
            }
            else
            {
                changed = true;
            }

            grid.ServerAssignPlayerToCell(playerId, cell);
            playerCells[playerId] = cell;

            if (changed)
                PlayerCellChanged?.Invoke(playerId, previous, cell);
        }


        if (playerCells.Count == observedThisFrame.Count)
            return;

        toRemove.Clear();
        foreach (var kv in playerCells)
        {
            Debug.Log($"{kv.Key}: {kv.Value}");
            if (!observedThisFrame.Contains(kv.Key))
            {
                Debug.Log($"{kv.Key}: {kv.Value}");
                toRemove.Add(kv.Key);
            }
        }

        foreach (var id in toRemove)
            RemovePlayer(id);
    }

    void EnsurePlayerRecorded(NetworkConnection id, Vector2Int cell)
    {
        if (!grid)
            return;

        var playersInCell = grid.GetPlayersInCell(cell.x, cell.y);
        foreach (var existing in playersInCell)
        {
            if (existing.Equals(id))
                return;
        }

        grid.ServerAssignPlayerToCell(id, cell);
    }

    void RemovePlayer(NetworkConnection id)
    {
        if (playerCells.TryGetValue(id, out var cell))
        {
            grid.ServerRemovePlayerFromCell(id, cell);
            playerCells.Remove(id);
            PlayerCellChanged?.Invoke(id, cell, null);
        }
    }

    Transform ResolveCharacterTransform(PlayerData player, out CharacterHealth state)
    {
        state = null;
        if (!player)
            return null;

        state = player.GetComponentInChildren<CharacterHealth>();
        if (state)
            return state.transform;

        return player.transform;
    }

    public bool TryGetPlayerCell(NetworkConnection player, out Vector2Int cell)
        => playerCells.TryGetValue(player, out cell);

    public bool TryGetPlayerWorldPosition(NetworkConnection player, out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;
        if (!playerCells.TryGetValue(player, out var cell) || !grid)
            return false;

        worldPosition = grid.CellToWorldCenter(cell.x, cell.y);
        return true;
    }
}
