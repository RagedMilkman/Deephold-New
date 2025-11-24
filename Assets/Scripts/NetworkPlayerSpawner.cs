using FishNet.Managing;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;
using FishNet.Object;

public sealed class NetworkPlayerSpawner : MonoBehaviour
{
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private FishNet.Object.NetworkObject playerPrefab;

    private void OnEnable()
    {
        if (networkManager == null)
            networkManager = FindObjectOfType<NetworkManager>();

        networkManager.ServerManager.OnRemoteConnectionState += HandleClientState;
    }

    private void OnDisable()
    {
        networkManager.ServerManager.OnRemoteConnectionState -= HandleClientState;
    }

    private void HandleClientState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        if (args.ConnectionState != RemoteConnectionState.Started)
            return;

        // SERVER ONLY
        if (!networkManager.IsServer)
            return;

        // Spawn actual networked player
        NetworkObject player = Instantiate(playerPrefab);
        networkManager.ServerManager.Spawn(player.gameObject, conn);
    }
}
