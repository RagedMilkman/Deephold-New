using System;
using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing.Client;
using FishNet.Managing.Server;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

/// <summary>
/// Replicates bone snapshots from the owner to remote ghost followers and can
/// optionally spawn the ghost locally on non-owner clients.
/// </summary>
// Run after IK/puppet systems so snapshots include their final pose updates.
[DefaultExecutionOrder(10000)]
public class BoneSnapshotReplicator : NetworkBehaviour
{
    [SerializeField] private Transform _rigRoot;
    [SerializeField] private Transform _characterRoot;
    [Header("Ghost")]
    [SerializeField, Tooltip("Optional ghost prefab to spawn on non-owner clients.")]
    private GameObject _ghostPrefab;
    [SerializeField, Tooltip("Toolbelt to mirror onto spawned ghost visualizers.")]
    private ToolbeltNetworked _toolbelt;
    [SerializeField] private float _sendRate = 30f;
    [SerializeField, Tooltip("Write snapshot send/receive info to the console.")]
    private bool _debugLogSnapshots;
    [SerializeField, Tooltip("Reset debug counters when a GhostFollower is attached.")]
    private bool _resetDebugCountersOnAttach = true;
    [SerializeField, Tooltip("If true, the server will generate snapshots for this object when it is the authority (eg. NPCs).")]
    private bool _serverDrivesSnapshots;

    private readonly List<Transform> _bones = new();
    private string[] _bonePaths;
    private float _sendTimer;

    private GhostFollower _ghostFollower;
    private GameObject _ghostInstance;
    private bool _spawnedGhostInternally;
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
        if (!_toolbelt) _toolbelt = GetComponentInChildren<ToolbeltNetworked>(true);
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

        // Re-broadcast to all clients. Clone arrays so pooled message buffers from
        // the incoming broadcast cannot be mutated while the server sends to
        // others.
        var relay = new BoneSnapshotMessage
        {
            ObjectId = msg.ObjectId,
            Timestamp = msg.Timestamp,
            Positions = Clone(msg.Positions),
            Rotations = Clone(msg.Rotations),
            BonePaths = Clone(msg.BonePaths),
            CharacterRootPosition = msg.CharacterRootPosition,
            CharacterRootRotation = msg.CharacterRootRotation
        };

        _server.Broadcast(relay, true, Channel.Unreliable);
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

        TrySpawnGhostFollower();
    }

    public override void OnStopClient()
    {
        if (_client != null)
            _client.UnregisterBroadcast<BoneSnapshotMessage>(Client_ReceiveSnapshot);

        DespawnGhostFollower();

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
            Positions = Clone(msg.Positions),
            Rotations = Clone(msg.Rotations),
            BonePaths = Clone(msg.BonePaths),
            CharacterRootPosition = msg.CharacterRootPosition,
            CharacterRootRotation = msg.CharacterRootRotation
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
        bool canSend = IsOwner || (IsServer && _serverDrivesSnapshots);

        if (!canSend)
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
        var rotations = new Quaternion[_bones.Count];

        for (int i = 0; i < _bones.Count; i++)
        {
            Transform bone = _bones[i];

            positions[i] = bone.localPosition;

            rotations[i] = bone.localRotation;
        }

        return new BoneSnapshot()
        {
            Timestamp = Time.timeAsDouble,
            Positions = positions,
            Rotations = rotations,
            BonePaths = _bonePaths,
            CharacterRootPosition = _characterRoot.position,
            CharacterRootRotation = _characterRoot.rotation
        };
    }

    private void SendSnapshot(BoneSnapshot snapshot)
    {
        BoneSnapshotMessage msg = new BoneSnapshotMessage()
        {
            ObjectId = (uint)NetworkObject.ObjectId,
            Timestamp = snapshot.Timestamp,
            Positions = snapshot.Positions,
            Rotations = snapshot.Rotations,
            BonePaths = snapshot.BonePaths,
            CharacterRootPosition = snapshot.CharacterRootPosition,
            CharacterRootRotation = snapshot.CharacterRootRotation
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
        _spawnedGhostInternally &= follower != null;

        if (_ghostFollower != null && _resetDebugCountersOnAttach)
        {
            ResetDebugCounters();
            _ghostFollower.ResetDebugCounters();
        }

        if (_ghostFollower != null)
        {
            while (_pendingSnapshots.Count > 0)
                _ghostFollower.EnqueueSnapshot(_pendingSnapshots.Dequeue());

            AssignToolbeltToGhost(_ghostFollower.gameObject);
        }
    }

    private void TrySpawnGhostFollower()
    {
        if (IsOwner || _ghostPrefab == null || _ghostInstance != null || _ghostFollower != null)
            return;

        _ghostInstance = Instantiate(_ghostPrefab);
        _ghostFollower = _ghostInstance.GetComponent<GhostFollower>();
        _spawnedGhostInternally = _ghostFollower != null;

        if (_ghostFollower == null)
        {
            Debug.LogWarning($"[BoneSnapshotReplicator] Ghost prefab '{_ghostPrefab.name}' is missing a GhostFollower component.");
            return;
        }

        SetGhostFollower(_ghostFollower);
        AssignToolbeltToGhost(_ghostInstance);
    }

    private void DespawnGhostFollower()
    {
        if (_spawnedGhostInternally && _ghostInstance != null)
            Destroy(_ghostInstance);

        _ghostInstance = null;
        _spawnedGhostInternally = false;
        if (_ghostFollower != null)
            SetGhostFollower(null);
    }

    private static T[] Clone<T>(T[] source)
    {
        if (source == null)
            return null;

        T[] copy = new T[source.Length];
        Array.Copy(source, copy, source.Length);
        return copy;
    }

    private void AssignToolbeltToGhost(GameObject ghostRoot)
    {
        if (ghostRoot == null)
            return;

        if (_toolbelt == null)
            _toolbelt = GetComponentInChildren<ToolbeltNetworked>(true);

        if (_toolbelt == null)
            return;

        foreach (ToolbeltVisualizer visualizer in ghostRoot.GetComponentsInChildren<ToolbeltVisualizer>(true))
        {
            if (visualizer != null)
                visualizer.SetSource(_toolbelt);
        }
    }
}
