using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Replicates position and rotation from the owner to observers for the ghost prefab.
/// </summary>
public class GhostMotor : NetworkBehaviour
{
    private static readonly Dictionary<NetworkConnection, GhostMotor> _ghostsByConnection = new();

    [SerializeField] private Transform _target;
    [SerializeField] private float _lerpRate = 12f;

    private Vector3 _replicatedPosition;
    private Quaternion _replicatedRotation;
    private NetworkConnection _ownerConnection;

    private void Awake()
    {
        if (!_target)
            _target = transform;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        if (_ownerConnection != null)
        {
            _ghostsByConnection[_ownerConnection] = this;
        }
        else
        {
            Debug.LogWarning($"GhostMotor on {name} started without an assigned owner connection; transforms will not sync until it is set.");
        }

        _replicatedPosition = _target.position;
        _replicatedRotation = _target.rotation;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();

        if (_ownerConnection != null)
            _ghostsByConnection.Remove(_ownerConnection);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (IsOwner)
            HideLocalGhostVisuals();

        _replicatedPosition = _target.position;
        _replicatedRotation = _target.rotation;
    }

    private void Update()
    {
        if (!IsOwner)
        {
            _target.SetPositionAndRotation(
                Vector3.Lerp(_target.position, _replicatedPosition, Time.deltaTime * _lerpRate),
                Quaternion.Slerp(_target.rotation, _replicatedRotation, Time.deltaTime * _lerpRate));
        }
    }

    [Server]
    private void ReceiveOwnerTransform(Vector3 position, Quaternion rotation)
    {
        _replicatedPosition = position;
        _replicatedRotation = rotation;
        _target.SetPositionAndRotation(position, rotation);
        BroadcastTransform(position, rotation);
    }

    [ObserversRpc(BufferLast = true)]
    private void BroadcastTransform(Vector3 position, Quaternion rotation)
    {
        _replicatedPosition = position;
        _replicatedRotation = rotation;
    }

    [Server]
    public void SetOwnerConnection(NetworkConnection connection)
    {
        _ownerConnection = connection;

        if (IsServer && _ownerConnection != null)
            _ghostsByConnection[_ownerConnection] = this;
    }

    [Server]
    public static void ApplyOwnerTransform(NetworkConnection connection, Vector3 position, Quaternion rotation)
    {
        if (connection == null)
            return;

        if (_ghostsByConnection.TryGetValue(connection, out GhostMotor ghost))
        {
            ghost.ReceiveOwnerTransform(position, rotation);
        }
        else
        {
            Debug.LogWarning($"No server ghost registered for connection {connection.ClientId}; unable to apply transform.");
        }
    }

    /// <summary>
    /// Prevent the local owner from seeing its own ghost while still allowing
    /// transform replication to run.
    /// </summary>
    private void HideLocalGhostVisuals()
    {
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;
    }
}
