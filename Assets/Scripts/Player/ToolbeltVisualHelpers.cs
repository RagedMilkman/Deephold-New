using System;
using UnityEngine;

internal static class ToolbeltVisualHelpers
{
    public static GameObject ApplySlotStance(
        ToolBeltSlot slot,
        ToolMountPoint.MountStance desiredStance,
        int slotIndex,
        Transform mountRoot,
        Func<int, float> getEquipDurationForSlot,
        Func<int, float> getUnequipDurationForSlot,
        Func<int, float> getStanceTransitionDurationForSlot,
        Action<Transform, ItemDefinition> applyDefinitionTransform,
        UnityEngine.Object context,
        float now)
    {
        if (slot == null)
            return null;

        var previousStance = slot.CurrentStance;
        float duration = CalculateStanceTransitionDuration(
            slotIndex,
            previousStance,
            desiredStance,
            getEquipDurationForSlot,
            getUnequipDurationForSlot,
            getStanceTransitionDurationForSlot);

        return slot.ApplyStance(desiredStance, mountRoot, applyDefinitionTransform, context, duration, now);
    }

    private static float CalculateStanceTransitionDuration(
        int slotIndex,
        ToolMountPoint.MountStance previousStance,
        ToolMountPoint.MountStance desiredStance,
        Func<int, float> getEquipDurationForSlot,
        Func<int, float> getUnequipDurationForSlot,
        Func<int, float> getStanceTransitionDurationForSlot)
    {
        if (previousStance == desiredStance)
            return 0f;

        return desiredStance switch
        {
            ToolMountPoint.MountStance.Away => getUnequipDurationForSlot(slotIndex),
            _ when previousStance == ToolMountPoint.MountStance.Away => getEquipDurationForSlot(slotIndex),
            _ => getStanceTransitionDurationForSlot(slotIndex),
        };
    }
}
