using UnityEngine;

/// <summary>
/// Describes how an item should be mounted and exposes its toolbelt slot category.
/// </summary>
[DisallowMultipleComponent]
public class ItemMountType : MonoBehaviour, IToolbeltItemCategoryProvider
{
    [SerializeField]
    private ToolMountPoint.MountType mountType = ToolMountPoint.MountType.Fallback;

    [Header("Equipping")]
    [SerializeField, Min(0f)]
    private float equipDuration = 0.25f;

    [SerializeField, Min(0f)]
    private float unequipDuration = 0.25f;

    [SerializeField, Min(0f)]
    private float stanceTransitionDuration = 0.1f;

    /// <summary>
    /// Mount classification that determines which tool mount points can host this item.
    /// </summary>
    public ToolMountPoint.MountType MountType => mountType;

    /// <inheritdoc />
    public ToolMountPoint.MountType ToolbeltMountType => mountType;

    /// <inheritdoc />
    public float ToolbeltEquipDuration => Mathf.Max(0f, equipDuration);

    /// <inheritdoc />
    public float ToolbeltUnequipDuration => Mathf.Max(0f, unequipDuration);

    /// <inheritdoc />
    public float ToolbeltStanceTransitionDuration => Mathf.Max(0f, stanceTransitionDuration);

    /// <summary>
    /// Toolbelt slot category for this item as provided by the shared category map.
    /// </summary>
    public ToolbeltSlotType ToolbeltCategory => MountTypeCategoryMap.ResolveCategory(mountType);
}
