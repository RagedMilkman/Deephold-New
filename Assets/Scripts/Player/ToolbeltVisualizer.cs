using System;
using System.Collections.Generic;
using UnityEngine;

public struct ToolbeltSnapshot : IEquatable<ToolbeltSnapshot>
{
    public int Slot0;
    public int Slot1;
    public int Slot2;
    public int Slot3;
    public int EquippedSlot;
    public ToolMountPoint.MountStance EquippedStance;

    public bool Equals(ToolbeltSnapshot other)
    {
        return Slot0 == other.Slot0
            && Slot1 == other.Slot1
            && Slot2 == other.Slot2
            && Slot3 == other.Slot3
            && EquippedSlot == other.EquippedSlot
            && EquippedStance == other.EquippedStance;
    }

    public override bool Equals(object obj)
    {
        return obj is ToolbeltSnapshot other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Slot0, Slot1, Slot2, Slot3, EquippedSlot, (int)EquippedStance);
    }
}

/// <summary>
/// Mirrors toolbelt visual state from a source ToolbeltNetworked onto a target avatar (local or ghost).
/// No gameplay logic is required on the target; this component simply renders items based on snapshots.
/// </summary>
public class ToolbeltVisualizer : MonoBehaviour
{
    private static readonly List<ToolbeltVisualizer> ActiveVisualizers = new();

    [Header("Source")]
    [SerializeField, Tooltip("Authoritative toolbelt to mirror (typically on the player/server).")]
    private ToolbeltNetworked source;
    [SerializeField, Tooltip("Disable rendering on the source toolbelt and only render via this visualizer.")]
    private bool hideSourceVisuals = true;

    [Header("Target Avatar")]
    [SerializeField] private Transform mountRoot;
    [SerializeField] private HumanoidRigAnimator humanoidRigAnimator;

    [Header("Registry (match order with source)")]
    [SerializeField] private List<ItemDefinition> itemRegistry = new();

    [Header("Visual Transitions")]
    [SerializeField, Min(0f)] private float defaultStanceTransitionDuration = 0.1f;

    [Header("Syncing")]
    [SerializeField, Min(0.02f)] private float syncIntervalSeconds = 0.1f;
    [SerializeField, Tooltip("Copy the source's item registry into the target view if empty.")]
    private bool copyRegistryFromSource = true;

    private ToolbeltSnapshot lastSnapshot;
    private float nextAllowedSyncTime;

    private ToolBeltSlot primarySlot;
    private ToolBeltSlot secondarySlot;
    private ToolBeltSlot tertiarySlot;
    private ToolBeltSlot consumableSlot;

    private ToolMountPoint[] mountPoints = Array.Empty<ToolMountPoint>();
    private GameObject equippedInstance;
    private KineticProjectileWeapon equippedWeapon;
    private ToolMountPoint.MountStance equippedStance = ToolMountPoint.MountStance.Passive;
    private int equippedSlot = ToolbeltNetworked.SlotCount;

    public ToolbeltNetworked Source => source;

    private void Awake()
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

    private void OnEnable()
    {
        if (!ActiveVisualizers.Contains(this))
            ActiveVisualizers.Add(this);

        ApplySourceVisualPreference();
    }

    private void OnDisable()
    {
        ActiveVisualizers.Remove(this);
    }

    private void OnDestroy()
    {
        ClearVisuals();
    }

    private void Update()
    {
        if (!source)
            return;

        ApplySourceVisualPreference();

        if (Time.time < nextAllowedSyncTime)
            return;

        nextAllowedSyncTime = Time.time + syncIntervalSeconds;

        ToolbeltSnapshot snapshot = source.CaptureSnapshot();

        if (copyRegistryFromSource && !HasRegistryEntries())
            CopyRegistry(source.ItemRegistry);

        if (!lastSnapshot.Equals(snapshot))
        {
            ApplySnapshot(snapshot);
            lastSnapshot = snapshot;
        }
    }

    private void LateUpdate()
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

        equippedSlot = Mathf.Clamp(snapshot.EquippedSlot, 1, ToolbeltNetworked.SlotCount);
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

    private bool HasRegistryEntries()
    {
        return itemRegistry != null && itemRegistry.Count > 0;
    }

    private void ApplySourceVisualPreference()
    {
        if (!source || !hideSourceVisuals)
            return;

        if (source.VisualsEnabled)
            source.VisualsEnabled = false;
    }

    private void RebuildVisual()
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

    private void EnsureSlotVisual(ToolBeltSlot slot)
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
            (instance, _) => AssignWeaponMountPoints(instance, slot.CurrentMount ?? mountRoot),
            null,
            this);
    }

    private void ApplyEquippedVisual(int oneBasedSlot)
    {
        int clampedSlot = Mathf.Clamp(oneBasedSlot, 1, ToolbeltNetworked.SlotCount);
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

            var instance = ToolbeltVisualHelpers.ApplySlotStance(
                slot,
                desiredStance,
                (int)slot.Slot,
                mountRoot,
                GetEquipDurationForSlot,
                GetUnequipDurationForSlot,
                GetStanceTransitionDurationForSlot,
                ApplyDefinitionTransform,
                this,
                now);
            if ((desiredStance == ToolMountPoint.MountStance.Passive
                || desiredStance == ToolMountPoint.MountStance.Active
                || desiredStance == ToolMountPoint.MountStance.Reloading) && instance)
                equippedInstance = instance;

            AssignWeaponMountPoints(instance, slot.CurrentMount ?? mountRoot);
        }
    }

    private void ClearVisuals()
    {
        foreach (var slot in EnumerateSlots())
            slot?.DestroyVisual(null, null);

        equippedInstance = null;
        equippedWeapon = null;
    }

    private void RefreshMountPoints()
    {
        mountPoints = mountRoot ? mountRoot.GetComponentsInChildren<ToolMountPoint>(true) : Array.Empty<ToolMountPoint>();
    }

    private void AssignWeaponMountPoints(GameObject instance, Transform mount)
    {
        if (!instance)
            return;

        var resolvedMount = mount ? mount : mountRoot;
        foreach (var weapon in instance.GetComponentsInChildren<KineticProjectileWeapon>(true))
            weapon.SetMountPoint(resolvedMount);
    }

    private void ApplyDefinitionTransform(Transform instanceTransform, ItemDefinition def)
    {
        if (!instanceTransform || def == null)
            return;

        instanceTransform.localPosition = def.localPosition;
        instanceTransform.localRotation = Quaternion.Euler(def.localEulerAngles);
        var scale = def.localScale;
        instanceTransform.localScale = (scale == Vector3.zero) ? Vector3.one : scale;
    }

    private ToolMountPoint.MountType DetermineMountType(ItemDefinition def)
    {
        if (!def?.prefab)
            return ToolMountPoint.MountType.Fallback;

        var provider = FindCategoryProvider(def.prefab);
        return provider != null ? provider.ToolbeltMountType : ToolMountPoint.MountType.Fallback;
    }

    private Transform ResolveMountTarget(ItemDefinition def, ToolMountPoint.MountType mountType, ToolMountPoint.MountStance stance)
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

        Debug.LogWarning($"ToolbeltVisualizer: defaulting {itemName} to mount root for {stance} stance", this);
        return mountRoot;
    }

    private Transform FindMountPoint(ToolMountPoint.MountType type, ToolMountPoint.MountStance stance)
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

    private IEnumerable<ToolBeltSlot> EnumerateSlots()
    {
        yield return primarySlot;
        yield return secondarySlot;
        yield return tertiarySlot;
        yield return consumableSlot;
    }

    private ToolBeltSlot GetSlotByIndex(int slot)
    {
        return slot switch
        {
            (int)ToolbeltSlotName.Primary => primarySlot,
            (int)ToolbeltSlotName.Secondary => secondarySlot,
            (int)ToolbeltSlotName.Tertiary => tertiarySlot,
            (int)ToolbeltSlotName.Consumable => consumableSlot,
            _ => null,
        };
    }

    private ItemDefinition GetSlot(int oneBasedSlot)
    {
        var slot = GetSlotState(oneBasedSlot);
        if (slot == null)
            return null;

        return GetRegistryDefinition(slot.RegistryIndex);
    }

    private ToolBeltSlot GetSlotState(int oneBasedSlot)
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

    private float GetEquipDurationForSlot(int oneBasedSlot)
    {
        var def = GetSlot(oneBasedSlot);
        return GetEquipDuration(def);
    }

    private float GetUnequipDurationForSlot(int oneBasedSlot)
    {
        var def = GetSlot(oneBasedSlot);
        return GetUnequipDuration(def);
    }

    private float GetEquipDuration(ItemDefinition def)
    {
        if (!def?.prefab)
            return 0f;

        var provider = FindCategoryProvider(def.prefab);
        return provider != null ? Mathf.Max(0f, provider.ToolbeltEquipDuration) : 0f;
    }

    private float GetUnequipDuration(ItemDefinition def)
    {
        if (!def?.prefab)
            return 0f;

        var provider = FindCategoryProvider(def.prefab);
        return provider != null ? Mathf.Max(0f, provider.ToolbeltUnequipDuration) : 0f;
    }

    private float GetStanceTransitionDurationForSlot(int oneBasedSlot)
    {
        var def = GetSlot(oneBasedSlot);
        return GetStanceTransitionDuration(def);
    }

    private float GetStanceTransitionDuration(ItemDefinition def)
    {
        if (!def?.prefab)
            return Mathf.Max(0f, defaultStanceTransitionDuration);

        var provider = FindCategoryProvider(def.prefab);
        return provider != null
            ? Mathf.Max(0f, provider.ToolbeltStanceTransitionDuration)
            : Mathf.Max(0f, defaultStanceTransitionDuration);
    }

    private IToolbeltItemCategoryProvider FindCategoryProvider(GameObject prefab)
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

    private ItemDefinition GetRegistryDefinition(int registryIndex)
    {
        if (itemRegistry == null)
            return null;

        if (registryIndex < 0 || registryIndex >= itemRegistry.Count)
            return null;

        return itemRegistry[registryIndex];
    }

    public void SetSource(ToolbeltNetworked newSource)
    {
        if (source == newSource)
            return;

        source = newSource;

        if (!isActiveAndEnabled)
            return;

        if (!source)
        {
            lastSnapshot = default;
            ClearVisuals();
            return;
        }

        ApplySourceVisualPreference();

        if (copyRegistryFromSource && !HasRegistryEntries())
            CopyRegistry(source.ItemRegistry);

        ToolbeltSnapshot snapshot = source.CaptureSnapshot();
        ApplySnapshot(snapshot);
        lastSnapshot = snapshot;
    }

    public static void PlayFireFeedbackForSource(
        ToolbeltNetworked source,
        int slot,
        int registryIndex,
        Vector3 origin,
        Vector3 endPoint,
        Vector3 hitNormal,
        bool hitSomething)
    {
        if (source == null)
            return;

        for (int i = 0; i < ActiveVisualizers.Count; i++)
        {
            var visualizer = ActiveVisualizers[i];
            if (visualizer == null || visualizer.Source != source)
                continue;

            visualizer.PlayFireFeedback(slot, registryIndex, origin, endPoint, hitNormal, hitSomething);
        }
    }

    private void PlayFireFeedback(int slot, int registryIndex, Vector3 origin, Vector3 endPoint, Vector3 hitNormal, bool hitSomething)
    {
        var targetSlot = GetSlotByIndex(slot);
        if (targetSlot == null || targetSlot.RegistryIndex != registryIndex)
            return;

        var instance = targetSlot.Instance;
        if (!instance)
            return;

        foreach (var weapon in instance.GetComponentsInChildren<KineticProjectileWeapon>(true))
            weapon.OnServerFired(origin, endPoint, hitNormal, hitSomething, suppressLocalFeedback: false);
    }
}
