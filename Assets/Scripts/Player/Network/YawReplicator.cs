using FishNet.Object;
using UnityEngine;

/// <summary>
/// Replicates a player's yaw to other clients using FishNet RPCs.
/// </summary>
public class YawReplicator : NetworkBehaviour
{
    [SerializeField] private Transform _rotateTarget;
    [SerializeField] private TopDownMotor _motor;
    [SerializeField, Tooltip("Minimum time between yaw sends from the owner.")]
    private float _sendInterval = 0.05f;
    [SerializeField, Tooltip("Degrees the yaw must change before forcing an update.")]
    private float _minDelta = 0.5f;

    private float _lastSentYaw;
    private float _lastSendTime;
    private float _replicatedYaw;

    private bool HasAuthority => IsOwner || IsServer;

    private void Awake()
    {
        if (!_rotateTarget) _rotateTarget = transform;
        if (!_motor) _motor = GetComponent<TopDownMotor>();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        _replicatedYaw = _rotateTarget ? _rotateTarget.eulerAngles.y : 0f;
        BroadcastYaw(_replicatedYaw);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyYaw(_replicatedYaw);
    }

    private void LateUpdate()
    {
        if (HasAuthority)
            return;

        ApplyYaw(_replicatedYaw);
    }

    /// <summary>
    /// Called by the owning client to push a yaw update. Throttled to reduce traffic.
    /// </summary>
    public void SubmitYaw(float yaw)
    {
        if (!HasAuthority)
            return;

        _replicatedYaw = yaw;

        if ((Time.time - _lastSendTime) < _sendInterval && Mathf.Abs(Mathf.DeltaAngle(_lastSentYaw, yaw)) < _minDelta)
            return;

        _lastSentYaw = yaw;
        _lastSendTime = Time.time;
        SendYawServer(yaw);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SendYawServer(float yaw)
    {
        _replicatedYaw = yaw;
        ApplyYaw(yaw);
        BroadcastYaw(yaw);
    }

    [ObserversRpc(BufferLast = true)]
    private void BroadcastYaw(float yaw)
    {
        if (HasAuthority)
            return;

        _replicatedYaw = yaw;
        ApplyYaw(yaw);
    }

    private void ApplyYaw(float yaw)
    {
        if (_motor)
            _motor.ApplyReplicatedYaw(yaw);
        else
            TopDownMotor.ApplyYawTo(_rotateTarget, yaw);
    }
}
