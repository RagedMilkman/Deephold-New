public enum ToolbeltSlotType
{
    None = 0,
    Primary = 1,
    Secondary = 2,
    Tertiary = 3,
    Consumable = 4,
}

public interface IToolbeltItemCategoryProvider
{
    ToolbeltSlotType ToolbeltCategory { get; }
    ToolMountPoint.MountType ToolbeltMountType { get; }
    float ToolbeltEquipDuration { get; }
    float ToolbeltUnequipDuration { get; }
    float ToolbeltStanceTransitionDuration { get; }
}
