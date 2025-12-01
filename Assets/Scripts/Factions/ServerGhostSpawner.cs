using FishNet.Object;
using UnityEngine;

/// <summary>
/// Spawns and links a ghost follower for server-authoritative characters on each client.
/// Mirrors the player ghost spawning flow but keeps the server as the sole owner.
/// </summary>
public sealed class ServerGhostSpawner : NetworkBehaviour
{
    [Header("Ghost Prefab")]
    [SerializeField, Tooltip("Prefab instantiated locally on clients as the visual ghost.")]
    private GameObject _ghostPrefab;
    [SerializeField, Tooltip("Replicator that feeds bone snapshots into the ghost follower.")]
    private BoneSnapshotReplicator _boneSnapshotReplicator;

    private GameObject _ghostInstance;
    private GhostFollower _ghostFollower;

    private void Awake()
    {
        if (_boneSnapshotReplicator == null)
            _boneSnapshotReplicator = GetComponentInChildren<BoneSnapshotReplicator>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // Server remains the owner; all clients should render a ghost.
        if (IsOwner)
            return;

        SpawnGhost();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();

        if (IsOwner)
            return;

        DespawnGhost();
    }

    private void SpawnGhost()
    {
        if (_ghostPrefab == null || _ghostInstance != null)
            return;

        _ghostInstance = Instantiate(_ghostPrefab);
        _ghostFollower = _ghostInstance.GetComponent<GhostFollower>();

        if (_boneSnapshotReplicator != null)
            _boneSnapshotReplicator.SetGhostFollower(_ghostFollower);
    }

    private void DespawnGhost()
    {
        if (_ghostInstance != null)
            Destroy(_ghostInstance);

        _ghostInstance = null;
        _ghostFollower = null;

        if (_boneSnapshotReplicator != null)
            _boneSnapshotReplicator.SetGhostFollower(null);
    }
}
