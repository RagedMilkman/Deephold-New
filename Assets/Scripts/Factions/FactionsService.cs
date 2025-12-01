using System.Collections.Generic;
using FishNet.Managing;
using UnityEngine;

/// <summary>
/// Central registry that tracks all factions on the server.
/// </summary>
public sealed class FactionsService : MonoBehaviour
{
    public static FactionsService Instance { get; private set; }

    private NetworkManager _networkManager;
    private readonly List<FactionController> _factions = new();

    public IReadOnlyList<FactionController> Factions => _factions;

    private void Awake()
    {
        _networkManager = FindObjectOfType<NetworkManager>();
        if (_networkManager != null && !_networkManager.IsServer)
        {
            enabled = false;
            return;
        }

        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"{nameof(FactionsService)} already exists in the scene. Destroying duplicate on {name}.");
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void RegisterFaction(FactionController faction)
    {
        if (faction == null || _factions.Contains(faction))
            return;

        _factions.Add(faction);
    }

    public void UnregisterFaction(FactionController faction)
    {
        if (faction == null)
            return;

        _factions.Remove(faction);
    }

    public FactionController FindFactionByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        foreach (FactionController faction in _factions)
        {
            if (faction != null && faction.gameObject.name == name)
                return faction;
        }

        return null;
    }
}
