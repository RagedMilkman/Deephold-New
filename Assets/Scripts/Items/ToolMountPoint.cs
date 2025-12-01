using UnityEngine;

/// <summary>
/// Component used to tag a tool mount with the type of equipment it accepts.
/// </summary>
public class ToolMountPoint : MonoBehaviour
{
    public enum MountType
    {
        Fallback,
        LargeRangedWeapon,
        SmallRangedWeapon,
        LargeMeleeWeapon,
        SmallMeleeWeapon,
    }

    public enum MountStance
    {
        Active,
        Passive,
        Away,
        Reloading,
    }

    [SerializeField]
    private MountType activeMountType = MountType.SmallRangedWeapon;

    [SerializeField]
    private MountStance passiveMountType = MountStance.Passive;

    /// <summary>
    /// Type of equipment this mount point is configured for when the wielder is in an active stance.
    /// </summary>
    public MountType ActiveType => activeMountType;

    /// <summary>
    /// Type of equipment this mount point is configured for when the wielder is in a passive stance.
    /// </summary>
    public MountStance PassiveType => passiveMountType;
}
