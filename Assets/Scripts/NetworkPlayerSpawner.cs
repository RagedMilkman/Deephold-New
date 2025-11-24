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

        if (_ownerPlayerPrefab == null || _ghostPlayerPrefab == null)
        {
            Debug.LogWarning("Player prefabs are not assigned on NetworkPlayerSpawner.");
            return;
        }

        FishNet.Object.NetworkObject ownerInstance = Instantiate(_ownerPlayerPrefab);
        _networkManager.ServerManager.Spawn(ownerInstance.gameObject, connection);

        FishNet.Object.NetworkObject ghostInstance = Instantiate(_ghostPlayerPrefab);

        // Prevent the owning client from observing the ghost while keeping it visible to others.
        GhostMotor ghostMotor = ghostInstance.GetComponent<GhostMotor>();
        if (ghostMotor != null)
            ghostMotor.ExcludeConnection(connection);

        _networkManager.ServerManager.Spawn(ghostInstance.gameObject, null);
    }
}
