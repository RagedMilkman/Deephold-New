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
    public Quaternion[] Rotations;
    public string[] BonePaths;
    public Vector3 CharacterRootPosition;
    public Quaternion CharacterRootRotation;

    public int BoneCount
    {
        get
        {
            int positionCount = Positions?.Length ?? 0;
            int rotationCount = Rotations?.Length ?? 0;
            return Mathf.Min(positionCount, rotationCount);
        }
    }
}

/// <summary>
/// Broadcast payload for transporting compressed bone data over FishNet custom messaging.
/// </summary>
public struct BoneSnapshotMessage : IBroadcast
{
    public uint ObjectId;
    public double Timestamp;
    public Vector3[] Positions;
    public Quaternion[] Rotations;
    public string[] BonePaths;
    public Vector3 CharacterRootPosition;
    public Quaternion CharacterRootRotation;

    public void Write(Writer writer)
    {
        writer.WriteUInt32(ObjectId);
        writer.WriteDouble(Timestamp);
        writer.WriteVector3(CharacterRootPosition);
        writer.Writequaternion(CharacterRootRotation);

        int count = (Positions != null && Rotations != null)
            ? Mathf.Min(Positions.Length, Rotations.Length)
            : 0;
        writer.WriteInt32(count);

        for (int i = 0; i < count; i++)
        {
            writer.WriteVector3(Positions[i]);
            writer.Writequaternion(Rotations[i]);
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
        CharacterRootRotation = reader.Readquaternion();

        int count = reader.ReadInt32();
        Positions = new Vector3[count];
        Rotations = new Quaternion[count];

        for (int i = 0; i < count; i++)
        {
            Positions[i] = reader.ReadVector3();
            Rotations[i] = reader.Readquaternion();
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

}
