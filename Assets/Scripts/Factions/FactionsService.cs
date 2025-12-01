using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

/// <summary>
/// Central registry that tracks all factions on the server.
/// </summary>
public sealed class FactionsService : MonoBehaviour
{
    public static FactionsService Instance { get; private set; }

    private NetworkManager _networkManager;
    private readonly List<FactionController> _factions = new();
    private bool _isServerActive;

    public IReadOnlyList<FactionController> Factions => _factions;

    private void Awake()
    {
        _networkManager = FindObjectOfType<NetworkManager>();

        SubscribeToNetworkEvents();
        if (_networkManager == null || _networkManager.IsServer)
            ActivateService();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        UnsubscribeFromNetworkEvents();
    }

    public void RegisterFaction(FactionController faction)
    {
        if (!_isServerActive || faction == null || _factions.Contains(faction))
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

    private void HandleServerConnectionState(ServerConnectionStateArgs args)
    {
        switch (args.ConnectionState)
        {
            case LocalConnectionState.Started:
                ActivateService();
                break;
            case LocalConnectionState.Stopping:
            case LocalConnectionState.Stopped:
                DeactivateService();
                break;
        }
    }

    private void ActivateService()
    {
        if (_isServerActive)
            return;

        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"{nameof(FactionsService)} already exists in the scene. Destroying duplicate on {name}.");
            Destroy(this);
            return;
        }

        Instance = this;
        _isServerActive = true;
    }

    private void DeactivateService()
    {
        if (!_isServerActive)
            return;

        _isServerActive = false;
        _factions.Clear();

        if (Instance == this)
            Instance = null;
    }

    private void SubscribeToNetworkEvents()
    {
        if (_networkManager == null)
            return;

        _networkManager.ServerManager.OnServerConnectionState += HandleServerConnectionState;
    }

    private void UnsubscribeFromNetworkEvents()
    {
        if (_networkManager == null)
            return;

        _networkManager.ServerManager.OnServerConnectionState -= HandleServerConnectionState;
    }
}
