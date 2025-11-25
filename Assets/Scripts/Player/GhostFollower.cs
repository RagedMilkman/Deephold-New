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

    private readonly List<Transform> _bones = new();
    private readonly List<BoneSnapshot> _snapshots = new();

    private void Awake()
    {
        if (!_skeletonRoot) _skeletonRoot = transform;
        BoneSnapshotUtility.CollectBones(_skeletonRoot, _bones);
        DisableGhostBehaviours();
    }

    public void EnqueueSnapshot(BoneSnapshot snapshot)
    {
        if (snapshot.Positions == null || snapshot.Forward == null || snapshot.Up == null)
            return;

        BoneSnapshotUtility.EnsureBoneList(_skeletonRoot, _bones);

        if (snapshot.BoneCount != _bones.Count)
            BoneSnapshotUtility.CollectBones(_skeletonRoot, _bones);

        _snapshots.Add(snapshot);
        if (_snapshots.Count > 5)
            _snapshots.RemoveAt(0);
    }

    private void LateUpdate()
    {
        if (_snapshots.Count == 0)
            return;

        BoneSnapshotUtility.EnsureBoneList(_skeletonRoot, _bones);

        double interpolationTime = Time.timeAsDouble - _interpolationBackTime;

        while (_snapshots.Count >= 2 && _snapshots[1].Timestamp <= interpolationTime)
            _snapshots.RemoveAt(0);

        var lhs = _snapshots[0];
        var rhs = (_snapshots.Count > 1) ? _snapshots[1] : lhs;

        double timeSpan = Mathf.Max(0.0001f, (float)(rhs.Timestamp - lhs.Timestamp));
        float t = (float)((interpolationTime - lhs.Timestamp) / timeSpan);
        t = Mathf.Clamp01(t);

        ApplySnapshot(lhs, rhs, t);
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
