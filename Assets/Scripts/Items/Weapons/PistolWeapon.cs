using UnityEngine;

public class PistolWeapon : KineticProjectileWeapon
{
    public override ToolbeltSlotType ToolbeltCategory => ToolbeltSlotType.Secondary;
    public override ToolMountPoint.MountType ToolbeltMountType => ToolMountPoint.MountType.SmallRangedWeapon;

}
