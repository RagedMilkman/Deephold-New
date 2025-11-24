using FishNet.Connection;
using FishNet.Object;
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

    private void Awake()
    {
        if (!_target)
            _target = transform;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

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
        if (connection == null)
            return;

        TargetDisableForConnection(connection);
    }

    [TargetRpc]
    private void TargetDisableForConnection(NetworkConnection connection)
    {
        gameObject.SetActive(false);
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
