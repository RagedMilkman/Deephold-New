using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Visual-only stand-in for a player's toolbelt. Does not own gameplay state or network logic;
/// it simply renders item prefabs on the provided mount points based on snapshots from the
/// authoritative toolbelt.
/// </summary>
public sealed class ToolbeltGhostView : MonoBehaviour
{
    public const int SlotCount = 4;

    [Header("Registry (match order with source)")]
    [SerializeField] private List<ItemDefinition> itemRegistry = new();

    [Header("Mounts")]
    [SerializeField] private Transform mountRoot;
    [SerializeField] private HumanoidRigAnimator humanoidRigAnimator;

    [Header("Visual Transitions")]
    [SerializeField, Min(0f)] private float defaultStanceTransitionDuration = 0.1f;

    private ToolBeltSlot primarySlot;
    private ToolBeltSlot secondarySlot;
    private ToolBeltSlot tertiarySlot;
    private ToolBeltSlot consumableSlot;

    private ToolMountPoint[] mountPoints = Array.Empty<ToolMountPoint>();
    private GameObject equippedInstance;
    private KineticProjectileWeapon equippedWeapon;
    private ToolMountPoint.MountStance equippedStance = ToolMountPoint.MountStance.Passive;
    private int equippedSlot = SlotCount;

    void Awake()
    {
        primarySlot = new ToolBeltSlot(ToolbeltSlotName.Primary, null);
        secondarySlot = new ToolBeltSlot(ToolbeltSlotName.Secondary, null);
        tertiarySlot = new ToolBeltSlot(ToolbeltSlotName.Tertiary, null);
        consumableSlot = new ToolBeltSlot(ToolbeltSlotName.Consumable, null);

        if (!humanoidRigAnimator && transform.root)
            humanoidRigAnimator = transform.root.GetComponentInChildren<HumanoidRigAnimator>(true);

        if (!mountRoot)
            mountRoot = humanoidRigAnimator ? humanoidRigAnimator.transform : transform;

        RefreshMountPoints();
    }

    void OnDestroy()
    {
        ClearVisuals();
    }

    void LateUpdate()
    {
        float now = Time.time;
        foreach (var slot in EnumerateSlots())
            slot?.UpdateVisual(now);
    }

    public void ApplySnapshot(in ToolbeltSnapshot snapshot)
    {
        primarySlot.RegistryIndex = snapshot.Slot0;
        secondarySlot.RegistryIndex = snapshot.Slot1;
        tertiarySlot.RegistryIndex = snapshot.Slot2;
        consumableSlot.RegistryIndex = snapshot.Slot3;

        equippedSlot = Mathf.Clamp(snapshot.EquippedSlot, 1, SlotCount);
        equippedStance = snapshot.EquippedStance;

        RebuildVisual();
    }

    public void CopyRegistry(IReadOnlyList<ItemDefinition> registry)
    {
        if (registry == null)
            return;

        itemRegistry.Clear();
        for (int i = 0; i < registry.Count; i++)
            itemRegistry.Add(registry[i]);
    }

    public bool HasRegistryEntries => itemRegistry != null && itemRegistry.Count > 0;

    void RebuildVisual()
    {
        if (!mountRoot)
            mountRoot = humanoidRigAnimator ? humanoidRigAnimator.transform : transform;

        if (!mountRoot)
            return;

        RefreshMountPoints();

        EnsureSlotVisual(primarySlot);
        EnsureSlotVisual(secondarySlot);
        EnsureSlotVisual(tertiarySlot);
        EnsureSlotVisual(consumableSlot);

        ApplyEquippedVisual(equippedSlot);
    }

    void EnsureSlotVisual(ToolBeltSlot slot)
    {
        if (slot == null)
            return;

        var def = GetRegistryDefinition(slot.RegistryIndex);
        slot.EnsureVisual(
            mountRoot,
            def,
            DetermineMountType,
            ResolveMountTarget,
            ApplyDefinitionTransform,
            null,
            AssignWeaponMountPoints,
            null,
            this);
    }

    void ApplyEquippedVisual(int oneBasedSlot)
    {
        int clampedSlot = Mathf.Clamp(oneBasedSlot, 1, SlotCount);
        equippedInstance = null;
        equippedWeapon = null;

        float now = Time.time;
        foreach (var slot in EnumerateSlots())
        {
            if (slot == null)
                continue;

            var desiredStance = (slot.Slot == (ToolbeltSlotName)clampedSlot)
                ? equippedStance
                : ToolMountPoint.MountStance.Away;

            var previousStance = slot.CurrentStance;
            float duration = 0f;

            if (previousStance != desiredStance)
            {
                if (desiredStance == ToolMountPoint.MountStance.Away)
                {
                    duration = GetUnequipDurationForSlot((int)slot.Slot);
                }
                else if (previousStance == ToolMountPoint.MountStance.Away)
                {
                    duration = GetEquipDurationForSlot((int)slot.Slot);
                }
                else
                {
                    duration = GetStanceTransitionDurationForSlot((int)slot.Slot);
                }
            }

            var instance = slot.ApplyStance(desiredStance, mountRoot, ApplyDefinitionTransform, this, duration, now);
            if ((desiredStance == ToolMountPoint.MountStance.Passive
                || desiredStance == ToolMountPoint.MountStance.Active
                || desiredStance == ToolMountPoint.MountStance.Reloading) && instance)
                equippedInstance = instance;

            AssignWeaponMountPoints(instance, slot.CurrentMount ?? mountRoot);
        }
    }

    void ClearVisuals()
    {
        foreach (var slot in EnumerateSlots())
            slot?.DestroyVisual(null, null);

        equippedInstance = null;
        equippedWeapon = null;
    }

    void RefreshMountPoints()
    {
        mountPoints = mountRoot ? mountRoot.GetComponentsInChildren<ToolMountPoint>(true) : Array.Empty<ToolMountPoint>();
    }

    void AssignWeaponMountPoints(GameObject instance, Transform mount)
    {
        if (!instance)
            return;

        var resolvedMount = mount ? mount : mountRoot;
        foreach (var weapon in instance.GetComponentsInChildren<KineticProjectileWeapon>(true))
            weapon.SetMountPoint(resolvedMount);
    }

    void ApplyDefinitionTransform(Transform instanceTransform, ItemDefinition def)
    {
        if (!instanceTransform || def == null)
            return;

        instanceTransform.localPosition = def.localPosition;
        instanceTransform.localRotation = Quaternion.Euler(def.localEulerAngles);
        var scale = def.localScale;
        instanceTransform.localScale = (scale == Vector3.zero) ? Vector3.one : scale;
    }

    ToolMountPoint.MountType DetermineMountType(ItemDefinition def)
    {
        if (!def?.prefab)
            return ToolMountPoint.MountType.Fallback;

        var provider = FindCategoryProvider(def.prefab);
        return provider != null ? provider.ToolbeltMountType : ToolMountPoint.MountType.Fallback;
    }

    Transform ResolveMountTarget(ItemDefinition def, ToolMountPoint.MountType mountType, ToolMountPoint.MountStance stance)
    {
        string itemName = def ? def.name : "<unknown>";

        Transform target = FindMountPoint(mountType, stance);
        if (target)
            return target;

        if (mountType != ToolMountPoint.MountType.Fallback)
        {
            if (stance != ToolMountPoint.MountStance.Away)
            {
                target = FindMountPoint(mountType, ToolMountPoint.MountStance.Away);
                if (target)
                    return target;
            }

            target = FindMountPoint(ToolMountPoint.MountType.Fallback, stance);
            if (target)
                return target;
        }

        Transform fallbackAway = FindMountPoint(ToolMountPoint.MountType.Fallback, ToolMountPoint.MountStance.Away);
        if (fallbackAway)
            return fallbackAway;

        Debug.LogWarning($"ToolbeltGhostView: defaulting {itemName} to mount root for {stance} stance", this);
        return mountRoot;
    }

    Transform FindMountPoint(ToolMountPoint.MountType type, ToolMountPoint.MountStance stance)
    {
        if (mountPoints == null)
            return null;

        for (int i = 0; i < mountPoints.Length; i++)
        {
            var point = mountPoints[i];
            if (!point)
                continue;

            if (point.ActiveType == type && point.PassiveType == stance)
                return point.transform;
        }

        return null;
    }

    IEnumerable<ToolBeltSlot> EnumerateSlots()
    {
        yield return primarySlot;
        yield return secondarySlot;
        yield return tertiarySlot;
        yield return consumableSlot;
    }

    ItemDefinition GetSlot(int oneBasedSlot)
    {
        var slot = GetSlotState(oneBasedSlot);
        if (slot == null)
            return null;

        return GetRegistryDefinition(slot.RegistryIndex);
    }

    ToolBeltSlot GetSlotState(int oneBasedSlot)
    {
        return oneBasedSlot switch
        {
            1 => primarySlot,
            2 => secondarySlot,
            3 => tertiarySlot,
            4 => consumableSlot,
            _ => null,
        };
    }

    float GetEquipDurationForSlot(int oneBasedSlot)
    {
        var def = GetSlot(oneBasedSlot);
        return GetEquipDuration(def);
    }

    float GetUnequipDurationForSlot(int oneBasedSlot)
    {
        var def = GetSlot(oneBasedSlot);
        return GetUnequipDuration(def);
    }

    float GetEquipDuration(ItemDefinition def)
    {
        if (!def?.prefab)
            return 0f;

        var provider = FindCategoryProvider(def.prefab);
        return provider != null ? Mathf.Max(0f, provider.ToolbeltEquipDuration) : 0f;
    }

    float GetUnequipDuration(ItemDefinition def)
    {
        if (!def?.prefab)
            return 0f;

        var provider = FindCategoryProvider(def.prefab);
        return provider != null ? Mathf.Max(0f, provider.ToolbeltUnequipDuration) : 0f;
    }

    float GetStanceTransitionDurationForSlot(int oneBasedSlot)
    {
        var def = GetSlot(oneBasedSlot);
        return GetStanceTransitionDuration(def);
    }

    float GetStanceTransitionDuration(ItemDefinition def)
    {
        if (!def?.prefab)
            return Mathf.Max(0f, defaultStanceTransitionDuration);

        var provider = FindCategoryProvider(def.prefab);
        return provider != null
            ? Mathf.Max(0f, provider.ToolbeltStanceTransitionDuration)
            : Mathf.Max(0f, defaultStanceTransitionDuration);
    }

    IToolbeltItemCategoryProvider FindCategoryProvider(GameObject prefab)
    {
        if (!prefab)
            return null;

        foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component is IToolbeltItemCategoryProvider provider)
                return provider;
        }

        return null;
    }

    ItemDefinition GetRegistryDefinition(int registryIndex)
    {
        if (itemRegistry == null)
            return null;

        if (registryIndex < 0 || registryIndex >= itemRegistry.Count)
            return null;

        return itemRegistry[registryIndex];
    }
}
