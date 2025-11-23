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
    [SerializeField] private FishNet.Object.NetworkObject _playerPrefab;

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
        if (args.ConnectionState != RemoteConnectionState.Started)
            return;

        if (_playerPrefab == null)
        {
            Debug.LogWarning("Player prefab is not assigned on NetworkPlayerSpawner.");
            return;
        }

        FishNet.Object.NetworkObject playerInstance = Instantiate(_playerPrefab);
        _networkManager.ServerManager.Spawn(playerInstance.gameObject, connection);
    }
}
