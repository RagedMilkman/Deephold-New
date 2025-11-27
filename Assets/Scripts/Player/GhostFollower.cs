using System.Collections.Generic;
using FishNet.Utility.Extension;
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
    [SerializeField, Tooltip("Verify that applied snapshots remain on the bones after assignment.")]
    private bool _verifyAfterApply;
    [SerializeField, Tooltip("Maximum allowed positional delta (world for root, local for children) when verifying."), Min(0f)]
    private float _verifyPositionTolerance = 0.001f;
    [SerializeField, Tooltip("Maximum allowed rotational delta in degrees when verifying."), Min(0f)]
    private float _verifyRotationTolerance = 0.5f;

    private readonly List<Transform> _bones = new();
    private readonly List<BoneSnapshot> _snapshots = new();
    private readonly Dictionary<string, Transform> _boneLookup = new();
    private readonly Dictionary<string, Transform> _boneNameLookup = new();

    private string[] _cachedBonePaths;
    private bool _loggedPathMismatch;

    private int _enqueuedSnapshots;
    private int _appliedSnapshots;
    private double _lastEnqueueTime;
    private double _lastApplyTime;

    private void Awake()
    {
        if (!_skeletonRoot) _skeletonRoot = transform;
        if (!_characterRoot) _characterRoot = transform;
        CollectBonesAndLookup();
        DisableGhostBehaviours();
    }

    public int EnqueuedSnapshots => _enqueuedSnapshots;
    public int AppliedSnapshots => _appliedSnapshots;
    public int BufferedSnapshots => _snapshots.Count;
    public double LastEnqueueTime => _lastEnqueueTime;
    public double LastApplyTime => _lastApplyTime;

    public void ResetDebugCounters()
    {
        _snapshots.Clear();
        _enqueuedSnapshots = 0;
        _appliedSnapshots = 0;
        _lastEnqueueTime = 0;
        _lastApplyTime = 0;
        _loggedPathMismatch = false;
    }

    public void EnqueueSnapshot(BoneSnapshot snapshot)
    {
        if (snapshot.Positions == null || snapshot.Forward == null || snapshot.Up == null)
            return;

        if (snapshot.BonePaths != null)
            EnsureBoneOrder(snapshot.BonePaths);
        else if (snapshot.BoneCount != _bones.Count)
            CollectBonesAndLookup();

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

            //  if (i == 0)
            //  {
            //      _bones[i].SetPositionAndRotation(blendedPosition, blendedRotation);
            //  }
            //  else
            //  {
            _bones[i].SetLocalPositionAndRotation(blendedPosition, blendedRotation);
           // _bones[i].localPosition = blendedPosition;
           //     _bones[i].localRotation = blendedRotation;
        //    }

            if (_verifyAfterApply)
            {
                Debug.Log("Verify");

                Vector3 currentPosition = _bones[i].localPosition;
                Quaternion currentRotation = _bones[i].localRotation;

                float positionError = Vector3.Distance(currentPosition, blendedPosition);
                float rotationError = Quaternion.Angle(currentRotation, blendedRotation);

                if (positionError > _verifyPositionTolerance || rotationError > _verifyRotationTolerance)
                {
                    string boneName = (_cachedBonePaths != null && i < _cachedBonePaths.Length)
                        ? _cachedBonePaths[i]
                        : _bones[i].name;
                    Debug.LogWarning(
                        $"[GhostFollower] Verification failed for '{boneName}' (index {i}): " +
                        $"pos error={positionError:F4}, rot error={rotationError:F2}deg (tolerance {_verifyPositionTolerance:F4}/{_verifyRotationTolerance:F2}).");
                }
            }
        }
    }

    private void CollectBonesAndLookup()
    {
        BoneSnapshotUtility.CollectBones(_skeletonRoot, _bones);
        _cachedBonePaths = BoneSnapshotUtility.CollectBonePaths(_skeletonRoot);

        _boneLookup.Clear();
        _boneNameLookup.Clear();
        for (int i = 0; i < _bones.Count; i++)
        {
            string path = _cachedBonePaths[i];
            if (!_boneLookup.ContainsKey(path))
                _boneLookup.Add(path, _bones[i]);

            string rootStripped = StripRoot(path);
            if (!_boneLookup.ContainsKey(rootStripped))
                _boneLookup.Add(rootStripped, _bones[i]);

            string name = _bones[i].name;
            if (!_boneNameLookup.ContainsKey(name))
                _boneNameLookup.Add(name, _bones[i]);
        }
    }

    private void EnsureBoneOrder(string[] paths)
    {
        if (paths.Length == 0)
            return;

        if (_cachedBonePaths != null && _cachedBonePaths.Length == paths.Length)
        {
            bool matches = true;
            for (int i = 0; i < paths.Length; i++)
            {
                if (_cachedBonePaths[i] != paths[i])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return;
        }

        var reordered = new List<Transform>(paths.Length);
        for (int i = 0; i < paths.Length; i++)
        {
            Transform resolved;
            if (_boneLookup.TryGetValue(paths[i], out resolved) || _boneLookup.TryGetValue(StripRoot(paths[i]), out resolved))
            {
                reordered.Add(resolved);
            }
            else
            {
                string terminalName = GetTerminalName(paths[i]);
                if (!string.IsNullOrEmpty(terminalName) && _boneNameLookup.TryGetValue(terminalName, out resolved))
                {
                    reordered.Add(resolved);
                    continue;
                }

                if (!_loggedPathMismatch)
                {
                    Debug.LogWarning($"[GhostFollower] Could not resolve bone path '{paths[i]}'. Falling back to local traversal order.");
                    _loggedPathMismatch = true;
                }

                CollectBonesAndLookup();
                return;
            }
        }

        _bones.Clear();
        _bones.AddRange(reordered);
        _cachedBonePaths = paths;
    }

    private static string StripRoot(string path)
    {
        int slashIndex = path.IndexOf('/') + 1;
        return (slashIndex <= 0 || slashIndex >= path.Length) ? path : path.Substring(slashIndex);
    }

    private static string GetTerminalName(string path)
    {
        int slashIndex = path.LastIndexOf('/') + 1;
        return (slashIndex <= 0 || slashIndex >= path.Length) ? path : path.Substring(slashIndex);
    }
}
