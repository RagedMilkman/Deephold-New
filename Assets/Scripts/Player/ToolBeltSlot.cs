using System;
using UnityEngine;

public enum ToolbeltSlotName
{
    Primary = 1,
    Secondary = 2,
    Tertiary = 3,
    Consumable = 4,
}

public sealed class ToolBeltSlot
{
    public ToolbeltSlotName Slot { get; }
    public ItemDefinition DefaultItem { get; }
    public int RegistryIndex { get; set; } = -1;

    private bool pendingExternalMountPose;
    private bool pendingHasExternalMountPose;
    private Vector3 pendingExternalMountPosition;
    private Quaternion pendingExternalMountRotation = Quaternion.identity;

    private ItemVisual visual;

    private sealed class ItemVisual
    {
        public int RegistryIndex;
        public ItemDefinition Definition;
        public GameObject Instance;
        public ToolMountPoint.MountType MountType;
        public ToolMountPoint.MountStance CurrentStance = ToolMountPoint.MountStance.Away;
        public Transform AwayMount;
        public Transform PassiveMount;
        public Transform ActiveMount;
        public Transform ReloadingMount;
        public Transform MountRoot;
        public Transform TargetMount;
        public ToolMountPoint.MountStance TargetStance = ToolMountPoint.MountStance.Away;
        public float TransitionStartTime;
        public float TransitionDuration;
        public Vector3 StartLocalPosition;
        public Quaternion StartLocalRotation = Quaternion.identity;
        public Vector3 StartLocalScale = Vector3.one;
        public Vector3 TargetLocalPosition;
        public Quaternion TargetLocalRotation = Quaternion.identity;
        public Vector3 TargetLocalScale = Vector3.one;
        public Vector3 TargetWorldPosition;
        public Quaternion TargetWorldRotation = Quaternion.identity;
        public bool HasExternalMountPose;
        public Vector3 ExternalMountPosition;
        public Quaternion ExternalMountRotation = Quaternion.identity;
        public Action<GameObject> OnDestroyed;
    }

    public ToolBeltSlot(ToolbeltSlotName slot, ItemDefinition defaultItem)
    {
        Slot = slot;
        DefaultItem = defaultItem;
    }

    public GameObject Instance => visual?.Instance;
    public bool HasInstance => visual?.Instance != null;
    public ToolMountPoint.MountStance CurrentStance => visual?.CurrentStance ?? ToolMountPoint.MountStance.Away;
    public Transform CurrentMount => visual?.TargetMount;
    public Transform CurrentMountRoot => visual?.MountRoot;

    public void EnsureVisual(
        Transform mountRoot,
        ItemDefinition definition,
        Func<ItemDefinition, ToolMountPoint.MountType> determineMountType,
        Func<ItemDefinition, ToolMountPoint.MountType, ToolMountPoint.MountStance, Transform> resolveMountTarget,
        Action<Transform, ItemDefinition> applyDefinitionTransform,
        Action<GameObject, bool> assignOwnerToolbelt,
        Action<GameObject> onInstanceCreated,
        Action<GameObject> onInstanceDestroyed,
        UnityEngine.Object context)
    {
        if (!mountRoot || RegistryIndex < 0 || definition?.prefab == null)
        {
            DestroyVisual(assignOwnerToolbelt, onInstanceDestroyed);
            return;
        }

        if (visual != null && visual.Instance != null && visual.RegistryIndex == RegistryIndex)
        {
            UpdateVisual(definition, determineMountType, resolveMountTarget, onInstanceDestroyed);
            return;
        }

        DestroyVisual(assignOwnerToolbelt, onInstanceDestroyed);

        var instance = UnityEngine.Object.Instantiate(definition.prefab, mountRoot);
        assignOwnerToolbelt?.Invoke(instance, true);

        visual = new ItemVisual
        {
            RegistryIndex = RegistryIndex,
            Definition = definition,
            Instance = instance,
            MountType = determineMountType != null ? determineMountType(definition) : ToolMountPoint.MountType.Fallback,
            CurrentStance = ToolMountPoint.MountStance.Away,
            MountRoot = mountRoot,
            OnDestroyed = onInstanceDestroyed,
            HasExternalMountPose = pendingHasExternalMountPose,
            ExternalMountPosition = pendingExternalMountPosition,
            ExternalMountRotation = pendingExternalMountRotation,
        };

        if (pendingExternalMountPose)
            visual.HasExternalMountPose = pendingHasExternalMountPose;

        onInstanceCreated?.Invoke(instance);
        UpdateMountTargets(definition, resolveMountTarget);
        ApplyStance(ToolMountPoint.MountStance.Away, mountRoot, applyDefinitionTransform, context, 0f, Time.time);
    }

    public GameObject ApplyStance(
        ToolMountPoint.MountStance stance,
        Transform mountRoot,
        Action<Transform, ItemDefinition> applyDefinitionTransform,
        UnityEngine.Object context,
        float duration = 0f,
        float now = 0f)
    {
        if (visual?.Instance == null)
            return null;

        duration = Mathf.Max(0f, duration);
        if (now <= 0f)
            now = Time.time;

        if (mountRoot && visual.MountRoot != mountRoot)
            visual.MountRoot = mountRoot;

        visual.CurrentStance = stance;
        visual.TargetStance = stance;
        visual.TargetMount = ResolveTargetMount(stance, mountRoot, context);
        RefreshTargetPose();

        var instanceTransform = visual.Instance.transform;

        if (visual.MountRoot && instanceTransform.parent != visual.MountRoot)
            instanceTransform.SetParent(visual.MountRoot, false);

        if (duration > 0f)
        {
            visual.TransitionStartTime = now;
            visual.TransitionDuration = duration;
            visual.StartLocalPosition = instanceTransform.localPosition;
            visual.StartLocalRotation = instanceTransform.localRotation;
            visual.StartLocalScale = instanceTransform.localScale;
        }
        else if (!(visual.TransitionDuration > 0f && visual.TargetStance == stance))
        {
            visual.TransitionDuration = 0f;
            ApplyTargetPose(instanceTransform);
        }

        return visual.Instance;
    }

    public GameObject DestroyVisual(Action<GameObject, bool> assignOwnerToolbelt, Action<GameObject> onInstanceDestroyed = null)
    {
        if (visual == null)
            return null;

        var instance = visual.Instance;
        if (instance)
        {
            (onInstanceDestroyed ?? visual.OnDestroyed)?.Invoke(instance);
            assignOwnerToolbelt?.Invoke(instance, false);
            UnityEngine.Object.Destroy(instance);
        }

        visual = null;
        return instance;
    }

    private void UpdateVisual(
        ItemDefinition definition,
        Func<ItemDefinition, ToolMountPoint.MountType> determineMountType,
        Func<ItemDefinition, ToolMountPoint.MountType, ToolMountPoint.MountStance, Transform> resolveMountTarget,
        Action<GameObject> onInstanceDestroyed)
    {
        if (visual == null)
            return;

        visual.RegistryIndex = RegistryIndex;
        visual.Definition = definition;
        if (determineMountType != null)
            visual.MountType = determineMountType(definition);

        if (onInstanceDestroyed != null)
            visual.OnDestroyed = onInstanceDestroyed;

        UpdateMountTargets(definition, resolveMountTarget);
    }

    private void UpdateMountTargets(
        ItemDefinition definition,
        Func<ItemDefinition, ToolMountPoint.MountType, ToolMountPoint.MountStance, Transform> resolveMountTarget)
    {
        if (visual == null)
            return;

        if (resolveMountTarget == null)
        {
            visual.AwayMount = null;
            visual.PassiveMount = null;
            visual.ActiveMount = null;
            return;
        }

        var type = visual.MountType;
        visual.AwayMount = resolveMountTarget(definition, type, ToolMountPoint.MountStance.Away);
        visual.PassiveMount = resolveMountTarget(definition, type, ToolMountPoint.MountStance.Passive);
        visual.ActiveMount = resolveMountTarget(definition, type, ToolMountPoint.MountStance.Active);
        visual.ReloadingMount = resolveMountTarget(definition, type, ToolMountPoint.MountStance.Reloading);
    }

    public void UpdateVisual(float now)
    {
        if (visual?.Instance == null)
            return;

        if (visual.MountRoot && visual.Instance.transform.parent != visual.MountRoot)
            visual.Instance.transform.SetParent(visual.MountRoot, false);

        RefreshTargetPose();

        var instanceTransform = visual.Instance.transform;

        if (visual.TransitionDuration > 0f)
        {
            float elapsed = Mathf.Max(0f, now - visual.TransitionStartTime);
            float t = visual.TransitionDuration > 0f ? Mathf.Clamp01(elapsed / visual.TransitionDuration) : 1f;
            instanceTransform.localPosition = Vector3.Lerp(visual.StartLocalPosition, visual.TargetLocalPosition, t);
            instanceTransform.localRotation = Quaternion.Slerp(visual.StartLocalRotation, visual.TargetLocalRotation, t);
            instanceTransform.localScale = Vector3.Lerp(visual.StartLocalScale, visual.TargetLocalScale, t);

            if (elapsed >= visual.TransitionDuration)
            {
                visual.TransitionDuration = 0f;
                ApplyTargetPose(instanceTransform);
            }
        }
        else
        {
            ApplyTargetPose(instanceTransform);
        }
    }

    private Transform ResolveTargetMount(ToolMountPoint.MountStance stance, Transform mountRoot, UnityEngine.Object context)
    {
        Transform target = stance switch
        {
            ToolMountPoint.MountStance.Active => visual.ActiveMount ?? visual.PassiveMount ?? visual.AwayMount,
            ToolMountPoint.MountStance.Passive => visual.PassiveMount ?? visual.AwayMount,
            ToolMountPoint.MountStance.Reloading => visual.ReloadingMount
                ?? visual.ActiveMount
                ?? visual.PassiveMount
                ?? visual.AwayMount,
            _ => visual.AwayMount,
        };

        if (target)
            return target;

        if (mountRoot)
        {
            Debug.LogWarning($"Toolbelt: no mount target for {visual.Definition?.name ?? "<unknown>"}; using mount root", context);
            return mountRoot;
        }

        if (visual.Instance.transform.parent)
            return visual.Instance.transform.parent;

        Debug.LogWarning($"Toolbelt: no mount target or root for {visual.Definition?.name ?? "<unknown>"}", context);
        return null;
    }

    public bool TryGetTargetMountPose(out Vector3 position, out Quaternion rotation, Transform mountRoot = null)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (visual == null)
            return false;

        Transform root = mountRoot ? mountRoot : visual.MountRoot;
        if (!root)
            return false;

        Transform target = visual.TargetMount ? visual.TargetMount : root;
        if (!target)
            return false;

        position = target.position;
        rotation = target.rotation;
        return true;
    }

    public void SetExternalMountPose(bool hasPose, Vector3 position, Quaternion rotation)
    {
        pendingExternalMountPose = true;
        pendingHasExternalMountPose = hasPose;
        pendingExternalMountPosition = position;
        pendingExternalMountRotation = rotation;

        if (visual != null)
        {
            visual.HasExternalMountPose = hasPose;
            if (hasPose)
            {
                visual.ExternalMountPosition = position;
                visual.ExternalMountRotation = rotation;
            }
        }
    }

    private void RefreshTargetPose()
    {
        if (visual == null)
            return;

        Transform root = visual.MountRoot ? visual.MountRoot : visual.Instance.transform.parent;
        if (!root)
            return;

        Vector3 mountWorldPos;
        Quaternion mountWorldRot;

        if (visual.HasExternalMountPose)
        {
            mountWorldPos = visual.ExternalMountPosition;
            mountWorldRot = visual.ExternalMountRotation;
        }
        else
        {
            Transform target = visual.TargetMount ? visual.TargetMount : root;
            mountWorldPos = target ? target.position : root.position;
            mountWorldRot = target ? target.rotation : root.rotation;
        }

        Vector3 offsetPos = visual.Definition ? visual.Definition.localPosition : Vector3.zero;
        Quaternion offsetRot = visual.Definition ? Quaternion.Euler(visual.Definition.localEulerAngles) : Quaternion.identity;
        Vector3 offsetScale = visual.Definition ? visual.Definition.localScale : Vector3.one;
        if (offsetScale == Vector3.zero)
            offsetScale = Vector3.one;

        Vector3 worldPos = mountWorldPos + mountWorldRot * offsetPos;
        Quaternion worldRot = mountWorldRot * offsetRot;

        visual.TargetLocalPosition = root.InverseTransformPoint(worldPos);
        visual.TargetLocalRotation = Quaternion.Inverse(root.rotation) * worldRot;
        visual.TargetLocalScale = offsetScale;

        visual.TargetWorldPosition = worldPos;
        visual.TargetWorldRotation = worldRot;
    }

    private void ApplyTargetPose(Transform instanceTransform)
    {
        if (visual.MountRoot && instanceTransform.parent != visual.MountRoot)
            instanceTransform.SetParent(visual.MountRoot, true);

        instanceTransform.SetPositionAndRotation(visual.TargetWorldPosition, visual.TargetWorldRotation);
        instanceTransform.localScale = visual.TargetLocalScale;
    }
}
