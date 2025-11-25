# Splitting root and skeletal transform replication

Goal: drive the ghost from a single streamed root transform plus a full bone stream—no double transforms.

## Root transform (no descendants)
- Put the `NetworkObject` on a top-level GameObject that has **only** the actor root transform.
- Add your movement replicator there (e.g., `NetworkTransform`, FishNet equivalent, or this project's `YawReplicator`).
- Keep the rig/bones as a child so the movement replicator never serializes bone data.
- Configure **`BoneSnapshotReplicator._rootTransform`** to whichever transform should provide the world pose (often that same top-level object).【F:Assets/Scripts/Player/BoneSnapshotReplicator.cs†L14-L28】【F:Assets/Scripts/Player/BoneSnapshotReplicator.cs†L142-L165】

That root stream supplies world position/rotation for both the player and the ghost. The ghost should **not** have its own NetworkTransform.

## Bone hierarchy (all descendants)
- On the rig root (top of the skeleton), add **`BoneSnapshotReplicator`** and set `_rigRoot` to that transform so all descendants are collected.【F:Assets/Scripts/Player/BoneSnapshotReplicator.cs†L15-L30】【F:Assets/Scripts/Player/BoneSnapshotReplicator.cs†L107-L133】
- On remote clients, add **`GhostFollower`** to the same rig root to interpolate the received snapshots over every bone. Configure `_rootTarget` for the world-space placement and `_skeletonRoot` for the local bones if they differ.【F:Assets/Scripts/Player/GhostFollower.cs†L10-L46】【F:Assets/Scripts/Player/GhostFollower.cs†L58-L105】

The bone stream does not move the actor in world space; it only shapes the hierarchy beneath the rig root.

## What gets serialized per snapshot
- **RootPosition** / **RootRotation**: world-space pose for the actor root, transmitted alongside the bone data but treated separately so movement smoothing/prediction can differ from bone interpolation.【F:Assets/Scripts/Player/BoneSnapshot.cs†L11-L37】
- **Positions / Forward / Up**: local-space bone data for the rig hierarchy beneath `_rigRoot`. All entries are local so PuppetMaster/IK can safely adjust without fighting world motion.【F:Assets/Scripts/Player/BoneSnapshotReplicator.cs†L142-L165】【F:Assets/Scripts/Player/GhostFollower.cs†L82-L105】

## How the ghost moves
1. The authoritative player root replicates via its movement component.
2. `BoneSnapshotReplicator` samples the chosen `_rootTransform` for world motion and `_rigRoot` for bone locals, broadcasting both together.【F:Assets/Scripts/Player/BoneSnapshotReplicator.cs†L14-L28】【F:Assets/Scripts/Player/BoneSnapshotReplicator.cs†L120-L176】
3. `GhostFollower` applies the root pose to `_rootTarget`, then interpolates bone locals onto `_skeletonRoot`'s hierarchy.

Result: one lightweight root transform stream for position/rotation, plus a separate full descendant stream for skeletal detail, with no duplicated movement on the ghost.
