# ServerGhostSpawner usage guide

This component mirrors the player ghost spawning flow but keeps the server authoritative for faction-controlled entities. It instantiates a local visual ghost on every non-owner client and links it to bone snapshot replication so animations and transforms stay in sync with the server-driven character.

## How it works
- `ServerGhostSpawner` is a `NetworkBehaviour` that runs on faction-controlled characters.
- On non-owner clients, `OnStartClient` calls `SpawnGhost()` to instantiate the configured ghost prefab. The server keeps ownership of the network object, but each client renders its own ghost.
- The ghost prefab must include a `GhostFollower` component. After instantiation, `ServerGhostSpawner` wires it into the `BoneSnapshotReplicator` so that replicated bone snapshots drive the ghost visuals.
- When the client disconnects from the object, `OnStopClient` calls `DespawnGhost()` to destroy the local ghost and clear the replicator reference.

## Setup steps
1. **Add the component**: Attach `ServerGhostSpawner` to the server-authoritative faction unit (the same GameObject that owns the `BoneSnapshotReplicator`).
2. **Assign the ghost prefab**: Provide a prefab reference in the `Ghost Prefab` field. The prefab must contain a `GhostFollower` component that matches your player ghost setup.
3. **Link the replicator**: Ensure a `BoneSnapshotReplicator` exists on the object or its children. If the serialized field is left empty, the spawner will auto-find the first `BoneSnapshotReplicator` in children during `Awake`.
4. **Network authority**: Leave ownership with the server. Clients should not take ownership; the spawner short-circuits on owner clients to avoid duplicate ghosts.
5. **Cleanup**: Nothing extra is required. `OnStopClient` automatically tears down the local ghost and clears the replicator reference.

## Validation checklist
- The faction unit prefab includes `ServerGhostSpawner` and `BoneSnapshotReplicator` components.
- The ghost prefab reference is set and contains `GhostFollower`.
- The server is the sole owner of the faction unit network object.
