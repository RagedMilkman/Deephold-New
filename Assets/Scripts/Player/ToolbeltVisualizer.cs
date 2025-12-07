using System;
using UnityEngine;

public struct ToolbeltSnapshot : IEquatable<ToolbeltSnapshot>
{
    public int Slot0;
    public int Slot1;
    public int Slot2;
    public int Slot3;
    public int EquippedSlot;
    public ToolMountPoint.MountStance EquippedStance;

    public bool Equals(ToolbeltSnapshot other)
    {
        return Slot0 == other.Slot0
            && Slot1 == other.Slot1
            && Slot2 == other.Slot2
            && Slot3 == other.Slot3
            && EquippedSlot == other.EquippedSlot
            && EquippedStance == other.EquippedStance;
    }

    public override bool Equals(object obj)
    {
        return obj is ToolbeltSnapshot other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Slot0, Slot1, Slot2, Slot3, EquippedSlot, (int)EquippedStance);
    }
}

/// <summary>
/// Mirrors toolbelt visual state from a source ToolbeltNetworked onto a target ToolbeltNetworked.
/// Intended for ghost or proxy avatars that should display the owner's equipped items without
/// needing the full toolbelt gameplay logic.
///
/// Usage: place a <see cref="ToolbeltNetworked"/> on the ghost (target) so it can render items, then
/// add this visualizer to pull snapshots from the owning player's toolbelt (source). The ghost's
/// toolbelt runs in visual-only mode; it does not need network authority because snapshots are
/// applied locally.
/// </summary>
public class ToolbeltVisualizer : MonoBehaviour
{
    [Header("Source/Target")]
    [SerializeField, Tooltip("Authoritative toolbelt to mirror (typically the owner/player).")]
    ToolbeltNetworked source;
    [SerializeField, Tooltip("Toolbelt on the ghost/proxy used only for rendering visuals.")]
    ToolbeltNetworked target;

    [Header("Syncing")]
    [SerializeField, Min(0.02f)] float syncIntervalSeconds = 0.1f;

    ToolbeltSnapshot lastSnapshot;
    float nextAllowedSyncTime;

    void Awake()
    {
        if (!target)
            target = GetComponent<ToolbeltNetworked>();
    }

    void Update()
    {
        if (!source || !target)
            return;

        if (Time.time < nextAllowedSyncTime)
            return;

        nextAllowedSyncTime = Time.time + syncIntervalSeconds;

        ToolbeltSnapshot snapshot = source.CaptureSnapshot();
        if (!lastSnapshot.Equals(snapshot))
        {
            target.ApplySnapshot(snapshot);
            lastSnapshot = snapshot;
        }
    }
}
