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
    [SerializeField] private float _sendRate = 30f;

    private readonly List<Transform> _bones = new();
    private float _sendTimer;

    private GhostFollower _ghostFollower;
    private readonly Queue<BoneSnapshot> _pendingSnapshots = new();

    private ClientManager _client;
    private ServerManager _server;

    private void Awake()
    {
        if (!_rigRoot) _rigRoot = transform;
        BoneSnapshotUtility.CollectBones(_rigRoot, _bones);
    }

    // ---------------------------------------------------------------------
    // SERVER
    // ---------------------------------------------------------------------
    public override void OnStartServer()
    {
        base.OnStartServer();

        _server = NetworkManager.ServerManager;

        // Server receives snapshots FROM clients (owners)
        _server.RegisterBroadcast<BoneSnapshotMessage>(Server_ReceiveSnapshot);
    }

    public override void OnStopServer()
    {
        if (_server != null)
            _server.UnregisterBroadcast<BoneSnapshotMessage>(Server_ReceiveSnapshot);

        base.OnStopServer();
    }

    /// <summary>
    /// SERVER callback: received snapshot from client owner.
    /// Must match (NetworkConnection sender, T message, Channel channel)
    /// </summary>
    private void Server_ReceiveSnapshot(NetworkConnection sender, BoneSnapshotMessage msg, Channel channel)
    {
        if (!IsServer || NetworkObject == null)
            return;

        // Validate — only accept from the actual owner.
        if (sender != Owner || msg.ObjectId != NetworkObject.ObjectId)
            return;

        // Re-broadcast to all clients
        _server.Broadcast(msg, true, Channel.Unreliable);
    }

    // ---------------------------------------------------------------------
    // CLIENT
    // ---------------------------------------------------------------------
    public override void OnStartClient()
    {
        base.OnStartClient();

        _client = NetworkManager.ClientManager;

        // client receives snapshots from server
        _client.RegisterBroadcast<BoneSnapshotMessage>(Client_ReceiveSnapshot);
    }

    public override void OnStopClient()
    {
        if (_client != null)
            _client.UnregisterBroadcast<BoneSnapshotMessage>(Client_ReceiveSnapshot);

        base.OnStopClient();
    }

    /// <summary>
    /// CLIENT callback: receives snapshot from server.
    /// Must match (T message, Channel channel)
    /// </summary>
    private void Client_ReceiveSnapshot(BoneSnapshotMessage msg, Channel channel)
    {
        if (IsOwner || NetworkObject == null)
            return;

        if (msg.ObjectId != NetworkObject.ObjectId)
            return;

        BoneSnapshot snapshot = new BoneSnapshot
        {
            Timestamp = msg.Timestamp,
            Positions = msg.Positions,
            Forward = msg.Forward,
            Up = msg.Up
        };

        if (_ghostFollower != null)
            _ghostFollower.EnqueueSnapshot(snapshot);
        else
            _pendingSnapshots.Enqueue(snapshot);
    }

    // ---------------------------------------------------------------------
    // SENDING FROM OWNER
    // ---------------------------------------------------------------------
    private void LateUpdate()
    {
        if (!IsOwner)
            return;

        if (_client == null)
            _client = NetworkManager?.ClientManager;
        if (_server == null)
            _server = NetworkManager?.ServerManager;

        _sendTimer += Time.deltaTime;
        float sendInterval = 1f / Mathf.Max(1f, _sendRate);

        if (_sendTimer < sendInterval)
            return;

        _sendTimer = 0f;

        BoneSnapshot snapshot = BuildSnapshot();
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

            positions[i] = (i == 0 ? bone.position : bone.localPosition);

            BoneSnapshotUtility.CompressRotation(
                (i == 0 ? bone.rotation : bone.localRotation),
                out forward[i], out up[i]
            );
        }

        return new BoneSnapshot()
        {
            Timestamp = Time.timeAsDouble,
            Positions = positions,
            Forward = forward,
            Up = up
        };
    }

    private void SendSnapshot(BoneSnapshot snapshot)
    {
        BoneSnapshotMessage msg = new BoneSnapshotMessage()
        {
            ObjectId = (uint)NetworkObject.ObjectId,
            Timestamp = snapshot.Timestamp,
            Positions = snapshot.Positions,
            Forward = snapshot.Forward,
            Up = snapshot.Up
        };

        if (IsServer)
            _server.Broadcast(msg, true, Channel.Unreliable);
        else
            _client.Broadcast(msg, Channel.Unreliable);
    }

    // ---------------------------------------------------------------------
    // GHOST FOLLOWER ATTACHMENT
    // ---------------------------------------------------------------------
    public void SetGhostFollower(GhostFollower follower)
    {
        _ghostFollower = follower;

        while (_pendingSnapshots.Count > 0)
            _ghostFollower.EnqueueSnapshot(_pendingSnapshots.Dequeue());
    }
}
