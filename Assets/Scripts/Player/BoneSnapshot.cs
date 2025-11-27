using System;
using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Serializing;
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
    public string[] BonePaths;
    public Vector3 CharacterRootPosition;
    public Vector3 CharacterRootForward;
    public Vector3 CharacterRootUp;

    public int BoneCount => Positions?.Length ?? 0;
}

/// <summary>
/// Broadcast payload for transporting compressed bone data over FishNet custom messaging.
/// </summary>
public struct BoneSnapshotMessage : IBroadcast
{
    public uint ObjectId;
    public double Timestamp;
    public Vector3[] Positions;
    public Vector3[] Forward;
    public Vector3[] Up;
    public string[] BonePaths;
    public Vector3 CharacterRootPosition;
    public Vector3 CharacterRootForward;
    public Vector3 CharacterRootUp;

    public void Write(Writer writer)
    {
        writer.WriteUInt32(ObjectId);
        writer.WriteDouble(Timestamp);
        writer.WriteVector3(CharacterRootPosition);
        writer.WriteVector3(CharacterRootForward);
        writer.WriteVector3(CharacterRootUp);

        int count = Positions?.Length ?? 0;
        writer.WriteInt32(count);

        for (int i = 0; i < count; i++)
        {
            writer.WriteVector3(Positions[i]);
            writer.WriteVector3(Forward[i]);
            writer.WriteVector3(Up[i]);
        }

        writer.WriteBoolean(BonePaths != null);
        if (BonePaths != null)
        {
            writer.WriteInt32(BonePaths.Length);
            for (int i = 0; i < BonePaths.Length; i++)
                writer.WriteString(BonePaths[i]);
        }
    }

    public void Read(Reader reader)
    {
        ObjectId = reader.ReadUInt32();
        Timestamp = reader.ReadDouble();
        CharacterRootPosition = reader.ReadVector3();
        CharacterRootForward = reader.ReadVector3();
        CharacterRootUp = reader.ReadVector3();

        int count = reader.ReadInt32();
        Positions = new Vector3[count];
        Forward = new Vector3[count];
        Up = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            Positions[i] = reader.ReadVector3();
            Forward[i] = reader.ReadVector3();
            Up[i] = reader.ReadVector3();
        }

        bool hasPaths = reader.ReadBoolean();
        if (hasPaths)
        {
            int pathCount = reader.ReadInt32();
            BonePaths = new string[pathCount];
            for (int i = 0; i < pathCount; i++)
                BonePaths[i] = reader.ReadString();
        }
    }
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

    public static string[] CollectBonePaths(Transform root)
    {
        var paths = new List<string>();
        CollectBonePathRecursive(root, root, paths);
        return paths.ToArray();
    }

    private static void CollectBonePathRecursive(Transform root, Transform current, List<string> paths)
    {
        paths.Add(GetPath(root, current));
        for (int i = 0; i < current.childCount; i++)
            CollectBonePathRecursive(root, current.GetChild(i), paths);
    }

    private static string GetPath(Transform root, Transform current)
    {
        if (current == root)
            return current.name;

        var stack = new Stack<string>();
        Transform cursor = current;
        while (cursor != null && cursor != root)
        {
            stack.Push(cursor.name);
            cursor = cursor.parent;
        }

        stack.Push(root.name);
        return string.Join("/", stack.ToArray());
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
        return Quaternion.LookRotation(forward.normalized, up.normalized);
    }
}
