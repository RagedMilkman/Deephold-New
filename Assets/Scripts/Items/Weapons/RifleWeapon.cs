using UnityEngine;

public class RifleWeapon : KineticProjectileWeapon
{
    public override ToolbeltSlotType ToolbeltCategory => ToolbeltSlotType.Primary;
    public override ToolMountPoint.MountType ToolbeltMountType => ToolMountPoint.MountType.LargeRangedWeapon;

}
