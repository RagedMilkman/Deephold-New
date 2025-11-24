using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Replicates position and rotation from the owner to observers for the ghost prefab.
/// </summary>
public class GhostMotor : NetworkBehaviour
{
    [SerializeField] private Transform _target;
    [SerializeField] private float _lerpRate = 12f;

    private Vector3 _replicatedPosition;
    private Quaternion _replicatedRotation;

    [SyncVar] private int _excludedClientId = -1;

    private void Awake()
    {
        if (!_target)
            _target = transform;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        NetworkManager networkManager = base.NetworkManager;
        NetworkConnection localConnection = networkManager != null ? networkManager.ClientManager.Connection : null;
        if (localConnection != null && localConnection.ClientId == _excludedClientId)
        {
            gameObject.SetActive(false);
            return;
        }

        _replicatedPosition = _target.position;
        _replicatedRotation = _target.rotation;
    }

    private void Update()
    {
        if (IsOwner)
        {
            SendTransform(_target.position, _target.rotation);
        }
        else
        {
            _target.SetPositionAndRotation(
                Vector3.Lerp(_target.position, _replicatedPosition, Time.deltaTime * _lerpRate),
                Quaternion.Slerp(_target.rotation, _replicatedRotation, Time.deltaTime * _lerpRate));
        }
    }

    /// <summary>
    /// Prevents a specific connection from observing this ghost instance.
    /// </summary>
    [Server]
    public void ExcludeConnection(NetworkConnection connection)
    {
        _excludedClientId = connection.ClientId;
    }

    [ServerRpc]
    private void SendTransform(Vector3 position, Quaternion rotation)
    {
        _replicatedPosition = position;
        _replicatedRotation = rotation;
        BroadcastTransform(position, rotation);
    }

    [ObserversRpc(BufferLast = true)]
    private void BroadcastTransform(Vector3 position, Quaternion rotation)
    {
        if (IsOwner)
            return;

        _replicatedPosition = position;
        _replicatedRotation = rotation;
    }
}
