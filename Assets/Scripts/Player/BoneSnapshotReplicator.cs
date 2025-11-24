using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing.Client;
using FishNet.Managing.Server;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

/// <summary>
/// Replicates bone snapshots from the owner to remote ghost followers.
/// </summary>
public class BoneSnapshotReplicator : NetworkBehaviour
{
    [SerializeField] private Transform _rigRoot;
    [SerializeField, Tooltip("How many snapshots to send per second from the owner.")]
    private float _sendRate = 30f;
    private readonly List<Transform> _bones = new();
    private float _sendTimer;
    private GhostFollower _ghostFollower;
    private readonly Queue<BoneSnapshot> _pendingSnapshots = new();
    private ClientManager _clientManager;
    private ServerManager _serverManager;

    private void Awake()
    {
        if (!_rigRoot) _rigRoot = transform;
        BoneSnapshotUtility.CollectBones(_rigRoot, _bones);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        _serverManager = NetworkManager?.ServerManager;
        _serverManager?.RegisterBroadcast<BoneSnapshotMessage>(OnServerReceivedSnapshot);
    }

    public override void OnStopServer()
    {
        _serverManager?.UnregisterBroadcast<BoneSnapshotMessage>(OnServerReceivedSnapshot);
        base.OnStopServer();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        _clientManager = NetworkManager?.ClientManager;
        _clientManager?.RegisterBroadcast<BoneSnapshotMessage>(OnClientReceivedSnapshot);
    }

    public override void OnStopClient()
    {
        _clientManager?.UnregisterBroadcast<BoneSnapshotMessage>(OnClientReceivedSnapshot);
        base.OnStopClient();
    }

    public void SetGhostFollower(GhostFollower follower)
    {
        _ghostFollower = follower;

        if (_ghostFollower != null)
        {
            while (_pendingSnapshots.Count > 0)
                _ghostFollower.EnqueueSnapshot(_pendingSnapshots.Dequeue());
        }
    }

    private void LateUpdate()
    {
        if (!IsOwner)
            return;

        if (_clientManager == null)
            _clientManager = NetworkManager?.ClientManager;
        if (_serverManager == null)
            _serverManager = NetworkManager?.ServerManager;

        _sendTimer += Time.deltaTime;
        float sendInterval = (_sendRate <= 0f) ? 0f : 1f / _sendRate;
        if (_sendTimer < sendInterval)
            return;

        _sendTimer = 0f;
        var snapshot = BuildSnapshot();
        SendSnapshot(snapshot);
    }

    private BoneSnapshot BuildSnapshot()
    {
        var positions = new Vector3[_bones.Count];
        var forward = new Vector3[_bones.Count];
        var up = new Vector3[_bones.Count];

        for (int i = 0; i < _bones.Count; i++)
        {
            Transform bone = _bones[i];
            positions[i] = (i == 0) ? bone.position : bone.localPosition;
            BoneSnapshotUtility.CompressRotation((i == 0) ? bone.rotation : bone.localRotation, out forward[i], out up[i]);
        }

        return new BoneSnapshot
        {
            Timestamp = Time.timeAsDouble,
            Positions = positions,
            Forward = forward,
            Up = up
        };
    }

    private void SendSnapshot(BoneSnapshot snapshot)
    {
        var message = new BoneSnapshotMessage
        {
            ObjectId = NetworkObject != null ? NetworkObject.ObjectId : 0,
            Timestamp = snapshot.Timestamp,
            Positions = snapshot.Positions,
            Forward = snapshot.Forward,
            Up = snapshot.Up
        };

        if (IsServer && _serverManager != null)
            _serverManager.Broadcast(message, Channel.Unreliable);

        if (!IsServer && _clientManager != null)
            _clientManager.Broadcast(message, Channel.Unreliable);
    }

    private void OnServerReceivedSnapshot(NetworkConnection sender, BoneSnapshotMessage message)
    {
        if (!IsServer || NetworkObject == null)
            return;

        if (sender != Owner || message.ObjectId != NetworkObject.ObjectId)
            return;

        if (_serverManager == null)
            _serverManager = NetworkManager?.ServerManager;

        _serverManager?.Broadcast(message, Channel.Unreliable);
    }

    private void OnClientReceivedSnapshot(BoneSnapshotMessage message)
    {
        if (IsOwner || NetworkObject == null)
            return;

        if (message.ObjectId != NetworkObject.ObjectId)
            return;

        var snapshot = new BoneSnapshot
        {
            Timestamp = message.Timestamp,
            Positions = message.Positions,
            Forward = message.Forward,
            Up = message.Up
        };

        if (_ghostFollower != null)
            _ghostFollower.EnqueueSnapshot(snapshot);
        else
            _pendingSnapshots.Enqueue(snapshot);
    }
}
