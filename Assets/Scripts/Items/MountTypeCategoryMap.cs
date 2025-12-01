using System;
using System.Collections.Generic;

/// <summary>
/// Provides a central mapping between <see cref="ToolMountPoint.MountType"/> values and
/// their associated <see cref="ToolbeltSlotType"/> categories.
/// </summary>
public static class MountTypeCategoryMap
{
    [Serializable]
    public struct Entry
    {
        public ToolMountPoint.MountType mountType;
        public ToolbeltSlotType category;
    }

    private static readonly Dictionary<ToolMountPoint.MountType, ToolbeltSlotType> lookup =
        new Dictionary<ToolMountPoint.MountType, ToolbeltSlotType>
        {
            { ToolMountPoint.MountType.Fallback, ToolbeltSlotType.None },
            { ToolMountPoint.MountType.LargeRangedWeapon, ToolbeltSlotType.Primary },
            { ToolMountPoint.MountType.SmallRangedWeapon, ToolbeltSlotType.Secondary },
            { ToolMountPoint.MountType.LargeMeleeWeapon, ToolbeltSlotType.Primary },
            { ToolMountPoint.MountType.SmallMeleeWeapon, ToolbeltSlotType.Secondary },
        };

    /// <summary>
    /// Enumerates the configured mapping entries.
    /// </summary>
    public static IReadOnlyDictionary<ToolMountPoint.MountType, ToolbeltSlotType> Entries => lookup;

    /// <summary>
    /// Returns the configured <see cref="ToolbeltSlotType"/> for the provided mount type.
    /// </summary>
    /// <param name="mountType">The mount type to resolve.</param>
    public static ToolbeltSlotType ResolveCategory(ToolMountPoint.MountType mountType)
    {
        return lookup.TryGetValue(mountType, out var category)
            ? category
            : ToolbeltSlotType.None;
    }

    /// <summary>
    /// Updates or adds a mapping entry at runtime.
    /// </summary>
    /// <param name="mountType">Mount type to associate.</param>
    /// <param name="category">Resulting toolbelt slot category.</param>
    public static void SetMapping(ToolMountPoint.MountType mountType, ToolbeltSlotType category)
    {
        lookup[mountType] = category;
    }

    /// <summary>
    /// Replaces the existing mapping with the provided collection.
    /// </summary>
    /// <param name="entries">Entries to load into the map.</param>
    public static void SetMappings(IEnumerable<Entry> entries)
    {
        if (entries == null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        lookup.Clear();
        foreach (var entry in entries)
        {
            lookup[entry.mountType] = entry.category;
        }
    }
}
