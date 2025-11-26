using System.Collections.Generic;
using RootMotion.Dynamics;
using RootMotion.FinalIK;
using UnityEngine;

/// <summary>
/// Local-only visual follower for remote player ghosts using buffered bone snapshots.
/// </summary>
public class GhostFollower : MonoBehaviour
{
    [SerializeField, Tooltip("Seconds to buffer before interpolating received snapshots.")]
    private float _interpolationBackTime = 0.05f;
    [SerializeField, Tooltip("Root transform that contains the ghost skeleton.")]
    private Transform _skeletonRoot;
    [SerializeField, Tooltip("Root transform of the character that should follow but not copy descendants.")]
    private Transform _characterRoot;
    [SerializeField, Tooltip("Write snapshot enqueue/apply info to the console.")]
    private bool _debugLogSnapshots;

    private readonly List<Transform> _bones = new();
    private readonly List<BoneSnapshot> _snapshots = new();

    private int _enqueuedSnapshots;
    private int _appliedSnapshots;
    private double _lastEnqueueTime;
    private double _lastApplyTime;

    private void Awake()
    {
        if (!_skeletonRoot) _skeletonRoot = transform;
        if (!_characterRoot) _characterRoot = transform;
        BoneSnapshotUtility.CollectBones(_skeletonRoot, _bones);
        DisableGhostBehaviours();
    }

    public int EnqueuedSnapshots => _enqueuedSnapshots;
    public int AppliedSnapshots => _appliedSnapshots;
    public int BufferedSnapshots => _snapshots.Count;
    public double LastEnqueueTime => _lastEnqueueTime;
    public double LastApplyTime => _lastApplyTime;

    public void EnqueueSnapshot(BoneSnapshot snapshot)
    {
        if (snapshot.Positions == null || snapshot.Forward == null || snapshot.Up == null)
            return;

        if (snapshot.BoneCount != _bones.Count)
            BoneSnapshotUtility.CollectBones(_skeletonRoot, _bones);

        _snapshots.Add(snapshot);
        if (_snapshots.Count > 5)
            _snapshots.RemoveAt(0);

        _enqueuedSnapshots++;
        _lastEnqueueTime = snapshot.Timestamp;

        if (_debugLogSnapshots)
        {
            Debug.Log($"[GhostFollower] Enqueued snapshot {_enqueuedSnapshots} at {_lastEnqueueTime:F3}s (buffer={_snapshots.Count}).");
        }
    }

    private void LateUpdate()
    {
        if (_snapshots.Count == 0)
            return;

        double interpolationTime = Time.timeAsDouble - _interpolationBackTime;

        while (_snapshots.Count >= 2 && _snapshots[1].Timestamp <= interpolationTime)
            _snapshots.RemoveAt(0);

        var lhs = _snapshots[0];
        var rhs = (_snapshots.Count > 1) ? _snapshots[1] : lhs;

        double timeSpan = Mathf.Max(0.0001f, (float)(rhs.Timestamp - lhs.Timestamp));
        float t = (float)((interpolationTime - lhs.Timestamp) / timeSpan);
        t = Mathf.Clamp01(t);

        ApplySnapshot(lhs, rhs, t);

        _appliedSnapshots++;
        _lastApplyTime = interpolationTime;

        if (_debugLogSnapshots)
        {
            Debug.Log($"[GhostFollower] Applied snapshot {_appliedSnapshots} at {_lastApplyTime:F3}s (t={t:F2}, buffer={_snapshots.Count}).");
        }
    }

    private void DisableGhostBehaviours()
    {
        foreach (PuppetMaster puppetMaster in GetComponentsInChildren<PuppetMaster>(true))
            puppetMaster.enabled = false;

        foreach (IK ik in GetComponentsInChildren<IK>(true))
            ik.enabled = false;

        foreach (Animator animator in GetComponentsInChildren<Animator>(true))
            animator.enabled = false;
    }

    private void ApplySnapshot(BoneSnapshot from, BoneSnapshot to, float t)
    {
        Vector3 characterPosition = Vector3.Lerp(from.CharacterRootPosition, to.CharacterRootPosition, t);
        Quaternion characterRotation = Quaternion.Slerp(
            BoneSnapshotUtility.DecompressRotation(from.CharacterRootForward, from.CharacterRootUp),
            BoneSnapshotUtility.DecompressRotation(to.CharacterRootForward, to.CharacterRootUp),
            t);
        _characterRoot.SetPositionAndRotation(characterPosition, characterRotation);

        int boneCount = Mathf.Min(_bones.Count, Mathf.Min(from.BoneCount, to.BoneCount));
        for (int i = 0; i < boneCount; i++)
        {
            Vector3 blendedPosition = Vector3.Lerp(from.Positions[i], to.Positions[i], t);
            Quaternion blendedRotation = Quaternion.Slerp(
                BoneSnapshotUtility.DecompressRotation(from.Forward[i], from.Up[i]),
                BoneSnapshotUtility.DecompressRotation(to.Forward[i], to.Up[i]),
                t);

            if (i == 0)
            {
                _bones[i].SetPositionAndRotation(blendedPosition, blendedRotation);
            }
            else
            {
                _bones[i].localPosition = blendedPosition;
                _bones[i].localRotation = blendedRotation;
            }
        }
    }
}
