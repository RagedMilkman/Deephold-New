using UnityEngine;

/// <summary>
/// Simple melee weapon that occupies the tertiary slot as a small blunt weapon.
/// </summary>
public class SmallBluntMelee : MeleeWeapon
{
    public override ToolbeltSlotType ToolbeltCategory => ToolbeltSlotType.Tertiary;
    public override ToolMountPoint.MountType ToolbeltMountType => ToolMountPoint.MountType.SmallMeleeWeapon;
}
