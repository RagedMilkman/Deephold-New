using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

/// <summary>
/// Spawns a player object for each connecting client.
/// </summary>
public sealed class NetworkPlayerSpawner : MonoBehaviour
{
    [SerializeField] private NetworkManager _networkManager;
    [SerializeField] private FishNet.Object.NetworkObject _ownerPlayerPrefab;
    [SerializeField] private FishNet.Object.NetworkObject _ghostPlayerPrefab;

    private readonly HashSet<int> _spawnedConnectionIds = new HashSet<int>();
    private bool _spawnedLocalPlayer;

    private void Awake()
    {
        if (_networkManager == null)
            _networkManager = FindObjectOfType<NetworkManager>();
    }

    private void OnEnable()
    {
        if (_networkManager != null)
            _networkManager.ServerManager.OnRemoteConnectionState += HandleRemoteConnectionState;
    }

    private void OnDisable()
    {
        if (_networkManager != null)
            _networkManager.ServerManager.OnRemoteConnectionState -= HandleRemoteConnectionState;
    }

    private void HandleRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
    {
        if (args.ConnectionState == RemoteConnectionState.Started)
            SpawnForConnection(connection);
        else
            _spawnedConnectionIds.Remove(connection.ClientId);
    }

    private void Update()
    {
        if (_spawnedLocalPlayer || _networkManager == null || !_networkManager.IsServer)
            return;

        NetworkConnection localConnection = _networkManager.ServerManager?.LocalConnection;
        if (localConnection == null)
            return;

        SpawnForConnection(localConnection);
        _spawnedLocalPlayer = true;
    }

    private void SpawnForConnection(NetworkConnection connection)
    {
        if (connection == null || _spawnedConnectionIds.Contains(connection.ClientId))
            return;

        if (_ownerPlayerPrefab == null || _ghostPlayerPrefab == null)
        {
            Debug.LogWarning("Player prefabs are not assigned on NetworkPlayerSpawner.");
            return;
        }

        FishNet.Object.NetworkObject ownerInstance = Instantiate(_ownerPlayerPrefab);
        _networkManager.ServerManager.Spawn(ownerInstance.gameObject, connection);

        FishNet.Object.NetworkObject ghostInstance = Instantiate(_ghostPlayerPrefab);
        GhostMotor ghostMotor = ghostInstance.GetComponent<GhostMotor>();
        if (ghostMotor != null)
            ghostMotor.SetOwnerConnection(connection);

        _networkManager.ServerManager.Spawn(ghostInstance.gameObject);

        _spawnedConnectionIds.Add(connection.ClientId);
    }
}
