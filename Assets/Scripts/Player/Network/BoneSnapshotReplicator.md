# BoneSnapshotReplicator ghost setup

`BoneSnapshotReplicator` can now spawn and attach a ghost on every non-owner client. Assign a ghost prefab that includes a `GhostFollower` component to the `Ghost Prefab` field; the replicator will instantiate it for remote clients and pipe bone snapshots into it automatically.

## Usage
1. **Add or select a BoneSnapshotReplicator** on the networked character (player or faction-controlled).
2. **Assign the ghost prefab** in the `Ghost Prefab` field. The prefab must contain a `GhostFollower` matching your rig hierarchy.
3. **Leave ownership untouched**. The component works for both client-owned and server-owned objects; only non-owners spawn the ghost.
4. **Cleanup is automatic**. When the client loses the object, the replicator tears down the spawned ghost and clears the follower reference.
