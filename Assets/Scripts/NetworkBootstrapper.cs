using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.InputSystem;

public class NetworkBootstrapper : MonoBehaviour
{
    [SerializeField] private NetworkManager _networkManager;

    private void Awake()
    {
        if (_networkManager == null)
            _networkManager = FindObjectOfType<NetworkManager>();
    }

    [ContextMenu("Start Host")]
    public void StartHost()
    {
        if (!_networkManager.IsServer && !_networkManager.IsClient)
        {
            _networkManager.ServerManager.StartConnection();
            _networkManager.ClientManager.StartConnection();
            Debug.Log("Started HOST (server + client)");
        }
    }

    [ContextMenu("Start Server")]
    public void StartServer()
    {
        if (!_networkManager.IsServer)
        {
            _networkManager.ServerManager.StartConnection();
            Debug.Log("Started SERVER");
        }
    }

    [ContextMenu("Start Client")]
    public void StartClient()
    {
        if (!_networkManager.IsClient)
        {
            _networkManager.ClientManager.StartConnection();
            Debug.Log("Started CLIENT");
        }
    }

    [ContextMenu("Stop All")]
    public void StopAll()
    {
        if (_networkManager.IsClient)
            _networkManager.ClientManager.StopConnection();

        if (_networkManager.IsServer)
            _networkManager.ServerManager.StopConnection(true);

        Debug.Log("Stopped SERVER/CLIENT");
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.f1Key.wasPressedThisFrame) StartHost();
        if (keyboard.f2Key.wasPressedThisFrame) StartServer();
        if (keyboard.f3Key.wasPressedThisFrame) StartClient();
        if (keyboard.f4Key.wasPressedThisFrame) StopAll();
    }
}
