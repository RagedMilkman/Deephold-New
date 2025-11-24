using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Contains a snapshot of bone transforms for network playback.
/// </summary>
public struct BoneSnapshot
{
    public double Timestamp;
    public Vector3[] Positions;
    public Vector3[] Forward;
    public Vector3[] Up;

    public int BoneCount => Positions?.Length ?? 0;
}

/// <summary>
/// Utility helpers for collecting and compressing bone transform data.
/// </summary>
public static class BoneSnapshotUtility
{
    /// <summary>
    /// Recursively collects a deterministic list of bones starting at <paramref name="root"/>.
    /// </summary>
    public static void CollectBones(Transform root, List<Transform> bones)
    {
        if (root == null)
            throw new ArgumentNullException(nameof(root));

        bones.Clear();
        CollectRecursive(root, bones);
    }

    private static void CollectRecursive(Transform current, List<Transform> bones)
    {
        bones.Add(current);
        for (int i = 0; i < current.childCount; i++)
            CollectRecursive(current.GetChild(i), bones);
    }

    /// <summary>
    /// Compresses a rotation into two direction vectors (forward/up) for 6-float transport.
    /// </summary>
    public static void CompressRotation(Quaternion rotation, out Vector3 forward, out Vector3 up)
    {
        forward = rotation * Vector3.forward;
        up = rotation * Vector3.up;
    }

    /// <summary>
    /// Decompresses a rotation encoded as forward and up vectors.
    /// </summary>
    public static Quaternion DecompressRotation(Vector3 forward, Vector3 up)
    {
        if (forward == Vector3.zero)
            forward = Vector3.forward;
        if (up == Vector3.zero)
            up = Vector3.up;
        return Quaternion.LookRotation(forward, up);
    }
}
