using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Object;
using System.Linq;
using UnityEngine;

public sealed class NetworkPlayerSpawner : MonoBehaviour
{
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private FishNet.Object.NetworkObject playerPrefab;

    private FactionController _playerFaction;

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
        var alteredPlayer = playerPrefab;
        var newTransform = new Vector3(alteredPlayer.transform.position.x, 2, alteredPlayer.transform.position.z);
        alteredPlayer.transform.SetPositionAndRotation(newTransform, alteredPlayer.transform.rotation);
        NetworkObject player = Instantiate(alteredPlayer);
        networkManager.ServerManager.Spawn(player.gameObject, conn);

        AssignPlayerToFaction(player);
    }

    private void AssignPlayerToFaction(NetworkObject player)
    {
        if (player == null)
            return;

        CharacterData characterData = player.GetComponent<CharacterData>();
        if (characterData == null)
            return;

        FactionController playerFaction = GetPlayerFaction();
        playerFaction?.AssignPlayerCharacter(characterData);
    }

    private FactionController GetPlayerFaction()
    {
        if (_playerFaction != null)
            return _playerFaction;

        if (FactionsService.Instance != null)
            _playerFaction = FactionsService.Instance.Factions.FirstOrDefault(f => f != null && f.IsPlayerFaction);

        if (_playerFaction == null)
            _playerFaction = FindObjectsOfType<FactionController>().FirstOrDefault(f => f.IsPlayerFaction);

        return _playerFaction;
    }
}
