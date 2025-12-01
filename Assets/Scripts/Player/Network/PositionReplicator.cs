using FishNet.Object;
using UnityEngine;

/// <summary>
/// Replicates a player's world position to other clients using FishNet RPCs.
/// </summary>
public class PositionReplicator : NetworkBehaviour
{
    [SerializeField] private Transform _target;
    [SerializeField] private TopDownMotor _motor;
    [SerializeField, Tooltip("Minimum time between position sends from the owner.")]
    private float _sendInterval = 0.05f;
    [SerializeField, Tooltip("Meters the position must change before forcing an update.")]
    private float _minDistance = 0.01f;

    private Vector3 _lastSentPosition;
    private float _lastSendTime;
    private Vector3 _replicatedPosition;

    private bool HasAuthority => IsOwner || IsServer;

    private void Awake()
    {
        if (!_target) _target = transform;
        if (!_motor) _motor = GetComponent<TopDownMotor>();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        _replicatedPosition = _target ? _target.position : Vector3.zero;
        BroadcastPosition(_replicatedPosition);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyPosition(_replicatedPosition);
    }

    private void LateUpdate()
    {
        if (HasAuthority)
            return;

        ApplyPosition(_replicatedPosition);
    }

    /// <summary>
    /// Called by the owning client to push a position update. Throttled to reduce traffic.
    /// </summary>
    public void SubmitPosition(Vector3 position)
    {
        if (!HasAuthority)
            return;

        _replicatedPosition = position;

        if ((Time.time - _lastSendTime) < _sendInterval &&
            (position - _lastSentPosition).sqrMagnitude < (_minDistance * _minDistance))
            return;

        _lastSentPosition = position;
        _lastSendTime = Time.time;
        SendPositionServer(position);
    }

    [ServerRpc]
    private void SendPositionServer(Vector3 position)
    {
        _replicatedPosition = position;
        ApplyPosition(position);
        BroadcastPosition(position);
    }

    [ObserversRpc(BufferLast = true)]
    private void BroadcastPosition(Vector3 position)
    {
        if (HasAuthority)
            return;

        _replicatedPosition = position;
        ApplyPosition(position);
    }

    private void ApplyPosition(Vector3 position)
    {
        if (_motor)
            _motor.ApplyReplicatedPosition(position);
        else if (_target)
            _target.position = position;
    }
}
