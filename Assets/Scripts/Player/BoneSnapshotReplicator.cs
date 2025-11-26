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
// Run after IK/puppet systems so snapshots include their final pose updates.
[DefaultExecutionOrder(10000)]
public class BoneSnapshotReplicator : NetworkBehaviour
{
    [SerializeField] private Transform _rigRoot;
    [SerializeField] private Transform _characterRoot;
    [SerializeField] private float _sendRate = 30f;
    [SerializeField, Tooltip("Write snapshot send/receive info to the console.")]
    private bool _debugLogSnapshots;
    [SerializeField, Tooltip("Reset debug counters when a GhostFollower is attached.")]
    private bool _resetDebugCountersOnAttach = true;

    private readonly List<Transform> _bones = new();
    private string[] _bonePaths;
    private float _sendTimer;

    private GhostFollower _ghostFollower;
    private readonly Queue<BoneSnapshot> _pendingSnapshots = new();

    private int _sentSnapshots;
    private int _receivedSnapshots;
    private double _lastSendTime;
    private double _lastReceiveTime;

    private ClientManager _client;
    private ServerManager _server;

    private void Awake()
    {
        if (!_rigRoot) _rigRoot = transform;
        if (!_characterRoot) _characterRoot = transform;
        BoneSnapshotUtility.CollectBones(_rigRoot, _bones);
        _bonePaths = BoneSnapshotUtility.CollectBonePaths(_rigRoot);
    }

    public int SentSnapshots => _sentSnapshots;
    public int ReceivedSnapshots => _receivedSnapshots;
    public double LastSendTime => _lastSendTime;
    public double LastReceiveTime => _lastReceiveTime;

    public void ResetDebugCounters()
    {
        _sentSnapshots = 0;
        _receivedSnapshots = 0;
        _lastSendTime = 0;
        _lastReceiveTime = 0;
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

        // Validate  only accept from the actual owner.
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

        // Use the local receipt time to keep interpolation in the follower on a
        // consistent clock. Remote client clocks are not synchronized, so
        // trusting the sender's Timestamp can lead to snapshots being treated as
        // permanently "in the future" and never advancing.
        BoneSnapshot snapshot = new BoneSnapshot
        {
            Timestamp = Time.timeAsDouble,
            Positions = msg.Positions,
            Forward = msg.Forward,
            Up = msg.Up,
            BonePaths = msg.BonePaths,
            CharacterRootPosition = msg.CharacterRootPosition,
            CharacterRootForward = msg.CharacterRootForward,
            CharacterRootUp = msg.CharacterRootUp
        };

        if (_ghostFollower != null)
            _ghostFollower.EnqueueSnapshot(snapshot);
        else
            _pendingSnapshots.Enqueue(snapshot);

        _receivedSnapshots++;
        _lastReceiveTime = snapshot.Timestamp;
        if (_debugLogSnapshots)
        {
            Debug.Log($"[BoneSnapshotReplicator] Received snapshot {_receivedSnapshots} at {_lastReceiveTime:F3}s for object {NetworkObject.ObjectId}.");
        }
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

        BoneSnapshotUtility.CompressRotation(
            _characterRoot.rotation,
            out Vector3 characterForward,
            out Vector3 characterUp);

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
            Up = up,
            BonePaths = _bonePaths,
            CharacterRootPosition = _characterRoot.position,
            CharacterRootForward = characterForward,
            CharacterRootUp = characterUp
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
            Up = snapshot.Up,
            BonePaths = snapshot.BonePaths,
            CharacterRootPosition = snapshot.CharacterRootPosition,
            CharacterRootForward = snapshot.CharacterRootForward,
            CharacterRootUp = snapshot.CharacterRootUp
        };

        if (IsServer)
            _server.Broadcast(msg, true, Channel.Unreliable);
        else
            _client.Broadcast(msg, Channel.Unreliable);

        _sentSnapshots++;
        _lastSendTime = snapshot.Timestamp;
        if (_debugLogSnapshots)
        {
            Debug.Log($"[BoneSnapshotReplicator] Sent snapshot {_sentSnapshots} at {_lastSendTime:F3}s for object {NetworkObject.ObjectId}.");
        }
    }

    // ---------------------------------------------------------------------
    // GHOST FOLLOWER ATTACHMENT
    // ---------------------------------------------------------------------
    public void SetGhostFollower(GhostFollower follower)
    {
        _ghostFollower = follower;

        if (_ghostFollower != null && _resetDebugCountersOnAttach)
        {
            ResetDebugCounters();
            _ghostFollower.ResetDebugCounters();
        }

        if (_ghostFollower != null)
        {
            while (_pendingSnapshots.Count > 0)
                _ghostFollower.EnqueueSnapshot(_pendingSnapshots.Dequeue());
        }
    }
}
