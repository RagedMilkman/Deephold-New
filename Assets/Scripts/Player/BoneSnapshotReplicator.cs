using System.Collections.Generic;
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
    private const byte SnapshotSequenceChannel = 1;

    private readonly List<Transform> _bones = new();
    private float _sendTimer;
    private GhostFollower _ghostFollower;
    private readonly Queue<BoneSnapshot> _pendingSnapshots = new();

    private void Awake()
    {
        if (!_rigRoot) _rigRoot = transform;
        BoneSnapshotUtility.CollectBones(_rigRoot, _bones);
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

        _sendTimer += Time.deltaTime;
        float sendInterval = (_sendRate <= 0f) ? 0f : 1f / _sendRate;
        if (_sendTimer < sendInterval)
            return;

        _sendTimer = 0f;
        var snapshot = BuildSnapshot();
        SendSnapshotServer(snapshot.Timestamp, snapshot.Positions, snapshot.Forward, snapshot.Up);
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

    [ServerRpc(Channel = Channel.Unreliable, SequenceChannel = SnapshotSequenceChannel)]
    private void SendSnapshotServer(double timestamp, Vector3[] positions, Vector3[] forward, Vector3[] up)
    {
        BroadcastSnapshot(timestamp, positions, forward, up);
    }

    [ObserversRpc(Channel = Channel.Unreliable, SequenceChannel = SnapshotSequenceChannel)]
    private void BroadcastSnapshot(double timestamp, Vector3[] positions, Vector3[] forward, Vector3[] up)
    {
        if (IsOwner)
            return;

        var snapshot = new BoneSnapshot
        {
            Timestamp = timestamp,
            Positions = positions,
            Forward = forward,
            Up = up
        };

        if (_ghostFollower != null)
            _ghostFollower.EnqueueSnapshot(snapshot);
        else
            _pendingSnapshots.Enqueue(snapshot);
    }
}
