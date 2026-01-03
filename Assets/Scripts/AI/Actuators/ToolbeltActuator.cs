using Assets.Scripts.Items.Weapons;
using UnityEngine;

/// <summary>
/// Drives toolbelt-related actions for AI.
/// </summary>
public class ToolbeltActuator : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private ToolbeltNetworked toolbelt;
    [SerializeField] private TopDownMotor motor;

    public IWeapon EquippedWeapon
    {
        get
        {
            if (!toolbelt)
                return null;

            if (toolbelt.ActiveWeapon)
                return toolbelt.ActiveWeapon;

            if (toolbelt.EquippedWeapon)
                return toolbelt.EquippedWeapon;

            var equippedObject = toolbelt.CurrentEquippedObject;
            if (equippedObject)
                return equippedObject.GetComponentInChildren<IWeapon>(true);

            return null;
        }
    }

    private void Awake()
    {
        if (!toolbelt)
        {
            toolbelt = GetComponent<ToolbeltNetworked>();
            if (!toolbelt && transform.root)
                toolbelt = transform.root.GetComponentInChildren<ToolbeltNetworked>(true);
        }

        if (!motor && transform.root)
            motor = transform.root.GetComponentInChildren<TopDownMotor>(true);
    }

    /// <summary>
    /// Toggle between active and passive stance for the equipped item.
    /// </summary>
    public void SetActiveStance(bool active)
    {
        if (motor)
            motor.SetActiveStance(active);

        if (!toolbelt)
            return;

        var stance = active ? ToolMountPoint.MountStance.Active : ToolMountPoint.MountStance.Passive;
        toolbelt.SetEquippedStance(stance);
    }

    /// <summary>
    /// Fire or use the currently equipped item if supported.
    /// </summary>
    public void ActivateEquippedItem()
    {
        if (!toolbelt || !toolbelt.IsEquippedReady)
            return;

        var weapon = toolbelt.ActiveWeapon ?? toolbelt.EquippedWeapon;
        if (weapon)
        {
            weapon.InteractionTick(true, false);
            return;
        }

        var equipped = toolbelt.CurrentEquippedObject;
        if (!equipped)
            return;

        var tool = equipped.GetComponentInChildren<IPlayerTool>(true);
        if (tool != null)
            tool.InteractionTick(true, true, false, false);
    }

    /// <summary>
    /// Request the toolbelt to swap to a specific slot (1-based).
    /// </summary>
    public void ChangeEquippedItem(int slotIndex)
    {
        if (!toolbelt)
            return;

        toolbelt.RequestEquip(slotIndex);
    }

    /// <summary>
    /// Ensure the equipped weapon has ammo, swapping to the first available slot with ammo (primary -> secondary -> tertiary).
    /// </summary>
    public bool EnsureEquippedWeaponHasAmmo()
    {
        if (!toolbelt)
            return false;

        var equippedWeapon = EquippedWeapon;
        if (equippedWeapon != null)
        {
            var ammoType = equippedWeapon.AmmoType;
            if (ammoType == AmmoType.None || toolbelt.HasAmmo(ammoType))
                return false;
        }

        int[] slotOrder = { 1, 2, 3 };

        foreach (int slot in slotOrder)
        {
            if (!toolbelt.SlotHasUsableAmmo(slot))
                continue;

            if (toolbelt.CurrentSlot != slot)
                toolbelt.RequestEquip(slot);

            return true;
        }

        return false;
    }
}
