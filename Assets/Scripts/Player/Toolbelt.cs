using System.Collections.Generic;
using UnityEngine;
using System;
using Assets.Scripts.Items.Weapons;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using RootMotion.FinalIK;
using FishNet.CodeGenerating;
using UnityEngine.Rendering;

public class ToolbeltNetworked : NetworkBehaviour
{
    public const int SlotCount = 4;

    [Header("Item registry (same order on all peers)")]
    [SerializeField] private List<ItemDefinition> itemRegistry;

    [Header("Defaults")]
    [SerializeField] private ItemDefinition defaultPrimary;   // slot 1
    [SerializeField] private ItemDefinition defaultSecondary; // slot 2
    [SerializeField] private ItemDefinition defaultTertiary;  // slot 3
    [SerializeField] private ItemDefinition defaultConsumable; // slot 4

    private ToolBeltSlot primarySlot;
    private ToolBeltSlot secondarySlot;
    private ToolBeltSlot tertiarySlot;
    private ToolBeltSlot consumableSlot;

    [Header("Stance")]
    [SerializeField] private TopDownMotor stanceSource;

    [Header("Visual Transitions")]
    [SerializeField, Min(0f)] private float defaultStanceTransitionDuration = 0.1f;
    [SerializeField, Min(0f)] private float equipPreviewDelayDuration = 0.25f;

    [Header("Debug")]
    [SerializeField] private bool enableEquipDebugLogs = false;

    [Header("Visualization")]
    [SerializeField] private bool renderVisuals = true;
    [SerializeField, Tooltip("Only render toolbelt visuals on the owning client.")]
    private bool renderVisualsIfOwner = true;

    private Transform mountRoot;
    [Header("Animation")]
    [SerializeField] private HumanoidRigAnimator humanoidRigAnimator;
    [SerializeField] private Transform defaultLeftWristTarget;
    [SerializeField] private Transform defaultRightWristTarget;
    private bool defaultLeftWristErrorLogged;
    private bool defaultRightWristErrorLogged;

    private Component puppetMasterPropRoot;
    private Type puppetMasterPropType;
    private Type puppetMasterPropRootType;

    private ToolMountPoint[] mountPoints = System.Array.Empty<ToolMountPoint>();
    private GameObject equippedInstance;
    private KineticProjectileWeapon equippedWeapon;
    private int equippedSlot = SlotCount; // 1..SlotCount
    private ToolMountPoint.MountStance equippedStance = ToolMountPoint.MountStance.Passive;

    private readonly Dictionary<HandMount.HandSide, ArmBinding> handIKSolvers = new();

    [AllowMutableSyncType]
    private SyncVar<int> slot0Net = new(-1);
    [AllowMutableSyncType]
    private SyncVar<int> slot1Net = new(-1);
    [AllowMutableSyncType]
    private SyncVar<int> slot2Net = new(-1);
    [AllowMutableSyncType]
    private SyncVar<int> slot3Net = new(-1);
    [AllowMutableSyncType]
    private SyncVar<int> equippedSlotNet = new(SlotCount);
    [AllowMutableSyncType]
    private SyncVar<int> equippedStanceNet = new((int)ToolMountPoint.MountStance.Passive);

    ToolMountPoint.MountStance desiredEquippedStance = ToolMountPoint.MountStance.Passive;
    bool desiredStanceDirty = false;

    readonly List<int> equipRequestQueue = new();
    bool isProcessingRequest = false;
    bool processingFromQueue = false;
    int activeRequestState = -1;
    float equipTransitionEndsAt = 0f;
    KineticProjectileWeapon reloadingWeapon;
    ToolMountPoint.MountStance stanceBeforeReloading = ToolMountPoint.MountStance.Passive;
    bool equippedWeaponReloading = false;
    float reloadStanceEndsAt = 0f;

    private struct ArmBinding
    {
        public FullBodyBipedIK solver;
        public IKEffector effector;
        public Transform defaultTarget;
    }

    void Awake()
    {
        primarySlot = new ToolBeltSlot(ToolbeltSlotName.Primary, defaultPrimary);
        secondarySlot = new ToolBeltSlot(ToolbeltSlotName.Secondary, defaultSecondary);
        tertiarySlot = new ToolBeltSlot(ToolbeltSlotName.Tertiary, defaultTertiary);
        consumableSlot = new ToolBeltSlot(ToolbeltSlotName.Consumable, defaultConsumable);

        if (!stanceSource && transform.root)
            stanceSource = transform.root.GetComponentInChildren<TopDownMotor>(true);

        if (!humanoidRigAnimator && transform.root)
            humanoidRigAnimator = transform.root.GetComponentInChildren<HumanoidRigAnimator>(true);

        ResolvePuppetMasterPropRoot();

    }

    public override void OnStartNetwork()
    {
        if (!humanoidRigAnimator && transform.root)
            humanoidRigAnimator = transform.root.GetComponentInChildren<HumanoidRigAnimator>(true);

        mountRoot = humanoidRigAnimator ? humanoidRigAnimator.transform : transform.root;
        if (!mountRoot)
        {
            Debug.LogError("Toolbelt: unable to resolve mount root", this);
            return;
        }

        ResolvePuppetMasterPropRoot();

        AttachStanceSource();
        RefreshMountPoints();
        CacheArmSolvers();
        EnsureDefaultWristTargets();
        UpdateHandTargetsFromEquippedInstance();

        if (IsServer)
        {
            SetSlotRegistryIndex(primarySlot, DetermineRegistryIndexForSlot(1, primarySlot.DefaultItem));
            SetSlotRegistryIndex(secondarySlot, DetermineRegistryIndexForSlot(2, secondarySlot.DefaultItem));
            SetSlotRegistryIndex(tertiarySlot, DetermineRegistryIndexForSlot(3, tertiarySlot.DefaultItem));
            SetSlotRegistryIndex(consumableSlot, DetermineRegistryIndexForSlot(4, consumableSlot.DefaultItem));

            equippedSlot = DetermineInitialEquippedSlot();

            ServerStoreSlotIndex(1, primarySlot.RegistryIndex);
            ServerStoreSlotIndex(2, secondarySlot.RegistryIndex);
            ServerStoreSlotIndex(3, tertiarySlot.RegistryIndex);
            ServerStoreSlotIndex(4, consumableSlot.RegistryIndex);
            ServerSyncEquipped(equippedSlot);
            ServerSyncEquippedStance(equippedStance);

            RPC_InitSlots(primarySlot.RegistryIndex, secondarySlot.RegistryIndex, tertiarySlot.RegistryIndex, consumableSlot.RegistryIndex, equippedSlot, (int)equippedStance);
        }
        else
        {
            UpdateSlotsFromSyncVars();
            int replicatedSlot = Mathf.Clamp(equippedSlotNet.Value, 1, SlotCount);
            if (replicatedSlot != equippedSlot)
                equippedSlot = replicatedSlot;

            var replicatedStance = (ToolMountPoint.MountStance)equippedStanceNet.Value;
            ApplyEquippedStanceInternal(replicatedStance, false);
        }
    }

    void OnDestroy()
    {
        DetachStanceSource();
        ClearVisuals();
    }

    public void RequestEquip(int oneBasedSlot)
    {
        Debug.Log("RequestEquip: " + oneBasedSlot);

        if (!IsOwner && !IsServer)
            return;


        Debug.Log("Can make change");

        oneBasedSlot = Mathf.Clamp(oneBasedSlot, 1, SlotCount);
        DebugLog($"RequestEquip({oneBasedSlot}) equipped={equippedSlot} stance={equippedStance} processing={isProcessingRequest} queue={DescribeQueue()}");

        if (!isProcessingRequest && equipRequestQueue.Count == 0 && oneBasedSlot == equippedSlot)
        {
            if (equippedStance == ToolMountPoint.MountStance.Away)
            {
                desiredEquippedStance = DetermineMotorDrivenStance();
                desiredStanceDirty = true;
                EnqueueEquipRequestWithDelay(oneBasedSlot);
            }
            else
            {
                EnqueueRequestState(0);
            }

            ProcessQueue();
            return;
        }

        TrimQueueToCurrent();

        if (ShouldQueueUnequipBefore(oneBasedSlot))
            EnqueueRequestState(0);

        EnqueueEquipRequestWithDelay(oneBasedSlot);
        ProcessQueue();
    }

    string DescribeQueue()
    {
        if (equipRequestQueue.Count == 0)
            return "[]";

        return "[" + string.Join(",", equipRequestQueue) + "]";
    }

    void EnqueueRequestState(int state)
    {
        if (equipRequestQueue.Count > 0 && equipRequestQueue[equipRequestQueue.Count - 1] == state)
            return;

        equipRequestQueue.Add(state);
        DebugLog($"Queued state {state}, queue now {DescribeQueue()}");
    }

    void EnqueueEquipRequestWithDelay(int slot)
    {
        EnqueueRequestState(-1);
        EnqueueRequestState(slot);
    }

    void TrimQueueToCurrent()
    {
        if (isProcessingRequest && processingFromQueue && equipRequestQueue.Count > 0)
        {
            int minimum = (activeRequestState == -1) ? 2 : 1;
            while (equipRequestQueue.Count > minimum)
                equipRequestQueue.RemoveAt(equipRequestQueue.Count - 1);
            return;
        }

        equipRequestQueue.Clear();
    }

    bool ShouldQueueUnequipBefore(int targetSlot)
    {
        if (targetSlot < 1)
            return false;

        if (isProcessingRequest)
        {
            if (!processingFromQueue)
                return false;
            int comparisonState = activeRequestState;
            if (comparisonState == -1 && equipRequestQueue.Count > 1)
                comparisonState = equipRequestQueue[1];

            if (comparisonState == 0)
                return false;
            if (comparisonState == targetSlot)
                return false;
            return true;
        }

        if (!HasEquippedItemActive())
            return false;

        return equippedSlot != targetSlot;
    }

    bool HasEquippedItemActive()
    {
        if (equippedSlot < 1 || equippedSlot > SlotCount)
            return false;

        if (equippedStance == ToolMountPoint.MountStance.Away)
            return false;

        var slot = GetSlotState(equippedSlot);
        return slot != null && slot.HasInstance;
    }

    void ProcessQueue()
    {
        if (!(IsOwner || IsServer))
            return;
        if (isProcessingRequest)
            return;
        if (equipRequestQueue.Count == 0)
            return;

        int state = equipRequestQueue[0];
        DebugLog($"ProcessQueue starting state {state} with queue {DescribeQueue()}");

        if (state == -1)
            StartQueueEquipDelay();
        else if (state == 0)
            StartQueueUnequip();
        else
            StartQueueEquip(state);
    }

    void StartQueueEquipDelay()
    {
        processingFromQueue = true;
        activeRequestState = -1;
        isProcessingRequest = true;

        float duration = Mathf.Max(0f, equipPreviewDelayDuration);
        equipTransitionEndsAt = Time.time + duration;

        DebugLog($"StartQueueEquipDelay duration={duration:F3} endsAt={equipTransitionEndsAt:F3}");
    }

    void StartQueueUnequip()
    {
        processingFromQueue = true;
        activeRequestState = 0;
        isProcessingRequest = true;

        if (!HasEquippedItemActive())
        {
            DebugLog("StartQueueUnequip found nothing to unequip");
            equipTransitionEndsAt = Time.time;
            return;
        }

        ForceEquippedStance(ToolMountPoint.MountStance.Away);

        float duration = GetUnequipDurationForSlot(equippedSlot);
        equipTransitionEndsAt = duration > 0f ? Time.time + duration : Time.time;
        DebugLog($"StartQueueUnequip duration={duration:F3} endsAt={equipTransitionEndsAt:F3}");
    }

    void StartQueueEquip(int targetSlot)
    {
        int clamped = Mathf.Clamp(targetSlot, 1, SlotCount);
        DebugLog($"StartQueueEquip slot={clamped}");

        StartEquipAnimationInternal(clamped, true, true);

        if (IsServer)
        {
            ServerSyncEquipped(clamped);
            RPC_SetEquipped(clamped);
            DebugLog("StartQueueEquip broadcast via RPC_SetEquipped");
        }
        else
        {
            RPC_RequestEquip(clamped);
            DebugLog("StartQueueEquip sent RPC_RequestEquip");
        }
    }

    void StartEquipAnimationInternal(int slot, bool fromQueue, bool updateDesiredStance)
    {
        processingFromQueue = fromQueue;
        activeRequestState = slot;
        isProcessingRequest = true;

        int clamped = Mathf.Clamp(slot, 1, SlotCount);
        equippedSlot = clamped;

        if (updateDesiredStance)
        {
            desiredEquippedStance = DetermineMotorDrivenStance();
        }
        else
        {
            desiredEquippedStance = ResolveEquippedStance();
        }
        desiredStanceDirty = true;

        RebuildVisual(clamped);

        float duration = GetEquipDurationForSlot(clamped);
        if (duration > 0f)
        {
            ApplyDesiredStance(true, false);
            equipTransitionEndsAt = Time.time + duration;
        }
        else
        {
            ApplyDesiredStance(false, true);
            equipTransitionEndsAt = Time.time;
        }

        DebugLog($"StartEquipAnimationInternal slot={clamped} duration={duration:F3} endsAt={equipTransitionEndsAt:F3} fromQueue={fromQueue}");
    }

    void CompleteProcessingState()
    {
        if (!isProcessingRequest)
            return;

        bool continueQueue = processingFromQueue;
        int finishedState = activeRequestState;

        if (finishedState > 0)
            ApplyDesiredStance(false, true);

        isProcessingRequest = false;
        processingFromQueue = false;
        equipTransitionEndsAt = 0f;
        activeRequestState = -1;

        if (continueQueue && equipRequestQueue.Count > 0)
            equipRequestQueue.RemoveAt(0);

        DebugLog($"CompleteProcessingState finished={finishedState} queue={DescribeQueue()}");

        if (continueQueue)
            ProcessQueue();
    }

    public void RequestSetSlotItem(int oneBasedSlot, ItemDefinition item)
    {
        if (!IsOwner) return;

        oneBasedSlot = Mathf.Clamp(oneBasedSlot, 1, SlotCount);
        int idx = -1;
        if (item)
        {
            if (!TryGetRegistryIndexForSlot(oneBasedSlot, item, out idx))
                return;
        }

        RPC_RequestSetSlotItem(oneBasedSlot, idx);
    }

    [ServerRpc]
    void RPC_RequestEquip(int oneBasedSlot)
    {
        var slot = GetSlotState(oneBasedSlot);
        if (slot == null)
            return;

        int clamped = Mathf.Clamp(oneBasedSlot, 1, SlotCount);

        StartEquipAnimationInternal(clamped, false, false);
        ServerSyncEquipped(clamped);
        RPC_SetEquipped(clamped);
        DebugLog($"Server RPC_RequestEquip processed slot {clamped}");
    }

    [ServerRpc]
    void RPC_RequestSetSlotItem(int oneBasedSlot, int registryIndex)
    {
        var slot = GetSlotState(oneBasedSlot);
        if (slot == null)
            return;

        if (registryIndex >= 0)
        {
            var def = GetRegistryDefinition(registryIndex);
            if (!DefinitionMatchesSlot(oneBasedSlot, def))
                return;
        }

        slot.RegistryIndex = registryIndex;
        ServerStoreSlotIndex(oneBasedSlot, registryIndex);
        RPC_SetSlots(primarySlot.RegistryIndex, secondarySlot.RegistryIndex, tertiarySlot.RegistryIndex, consumableSlot.RegistryIndex);
        RPC_SetEquipped(equippedSlot);
    }

    [ObserversRpc]
    void RPC_InitSlots(int s0, int s1, int s2, int s3, int equipped, int stance)
    {
        primarySlot.RegistryIndex = s0;
        secondarySlot.RegistryIndex = s1;
        tertiarySlot.RegistryIndex = s2;
        consumableSlot.RegistryIndex = s3;

        if (IsServer)
        {
            ServerStoreSlotIndex(1, s0);
            ServerStoreSlotIndex(2, s1);
            ServerStoreSlotIndex(3, s2);
            ServerStoreSlotIndex(4, s3);
            ServerSyncEquipped(equipped);
            ServerSyncEquippedStance((ToolMountPoint.MountStance)stance);
        }

        equippedSlot = Mathf.Clamp(equipped, 1, SlotCount);
        ApplyEquippedStanceInternal((ToolMountPoint.MountStance)stance, false);
        RebuildVisual(equippedSlot);
    }

    [ObserversRpc]
    void RPC_SetSlots(int s0, int s1, int s2, int s3)
    {
        primarySlot.RegistryIndex = s0;
        secondarySlot.RegistryIndex = s1;
        tertiarySlot.RegistryIndex = s2;
        consumableSlot.RegistryIndex = s3;

        if (IsServer)
        {
            ServerStoreSlotIndex(1, s0);
            ServerStoreSlotIndex(2, s1);
            ServerStoreSlotIndex(3, s2);
            ServerStoreSlotIndex(4, s3);
        }

        RebuildVisual(equippedSlot);
    }

    [ObserversRpc]
    void RPC_SetEquipped(int oneBasedSlot)
    {
        int clamped = Mathf.Clamp(oneBasedSlot, 1, SlotCount);

        if (IsServer)
        {
            ServerSyncEquipped(clamped);
            return;
        }

        if (IsOwner)
        {
            DebugLog($"RPC_SetEquipped({oneBasedSlot}) ignored on owner");
            return;
        }

        DebugLog($"RPC_SetEquipped({oneBasedSlot}) -> {clamped}");
        StartEquipAnimationInternal(clamped, false, false);
    }

    void RebuildVisual(int oneBasedSlot)
    {
        if (!ShouldRenderVisuals || !IsClient)
            return;

        if (!EnsureMountRoot())
            return;

        RefreshMountPoints();
        CacheArmSolvers();

        EnsureSlotVisual(primarySlot);
        EnsureSlotVisual(secondarySlot);
        EnsureSlotVisual(tertiarySlot);
        EnsureSlotVisual(consumableSlot);

        ApplyEquippedVisual(oneBasedSlot);
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
            AssignOwnerToolbelt,
            RegisterInstanceWithPuppetMaster,
            UnregisterInstanceFromPuppetMaster,
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
                ? ResolveEquippedStance()
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

        UpdateHandTargetsFromEquippedInstance();

        UpdateEquippedWeaponReference();

        if (reloadingWeapon && reloadingWeapon != equippedWeapon)
        {
            reloadingWeapon = null;
            equippedWeaponReloading = false;
        }
    }

    void ClearVisuals()
    {
        foreach (var slot in EnumerateSlots())
            slot?.DestroyVisual(AssignOwnerToolbelt, UnregisterInstanceFromPuppetMaster);

        equippedInstance = null;
        equippedWeapon = null;
        reloadingWeapon = null;
        equippedWeaponReloading = false;
        reloadStanceEndsAt = 0f;

        ResetArmTargets();
    }

    void AssignOwnerToolbelt(GameObject instance, bool assign)
    {
        if (!instance)
            return;

        foreach (var weapon in instance.GetComponentsInChildren<KineticProjectileWeapon>(true))
            weapon.SetOwnerToolbelt(assign ? this : null);
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

    void ResolvePuppetMasterPropRoot()
    {
        puppetMasterPropType ??= Type.GetType("RootMotion.Dynamics.Prop, Assembly-CSharp-firstpass")
            ?? Type.GetType("RootMotion.Dynamics.Prop");
        puppetMasterPropRootType ??= Type.GetType("RootMotion.Dynamics.PropRoot, Assembly-CSharp-firstpass")
            ?? Type.GetType("RootMotion.Dynamics.PropRoot");

        if (puppetMasterPropRootType == null || !transform.root)
            return;

        puppetMasterPropRoot = transform.root.GetComponentInChildren(puppetMasterPropRootType, true);
    }

    void RegisterInstanceWithPuppetMaster(GameObject instance)
    {
        if (!instance || puppetMasterPropRoot == null || puppetMasterPropType == null)
            return;

        var prop = instance.GetComponent(puppetMasterPropType);
        if (prop == null)
            return;

        puppetMasterPropRoot.SendMessage("AddProp", prop, SendMessageOptions.DontRequireReceiver);
    }

    void UnregisterInstanceFromPuppetMaster(GameObject instance)
    {
        if (!instance || puppetMasterPropRoot == null || puppetMasterPropType == null)
            return;

        var prop = instance.GetComponent(puppetMasterPropType);
        if (prop == null)
            return;

        puppetMasterPropRoot.SendMessage("RemoveProp", prop, SendMessageOptions.DontRequireReceiver);
    }

    ToolMountPoint.MountType DetermineMountType(ItemDefinition def)
    {
        if (!def?.prefab)
        {
            Debug.Log("DetermineMountType prefab is null");
            return ToolMountPoint.MountType.Fallback;
        }            

        var provider = FindCategoryProvider(def.prefab);
        return provider != null ? provider.ToolbeltMountType : ToolMountPoint.MountType.Fallback;
    }

    void RefreshMountPoints()
    {
        mountPoints = mountRoot ? mountRoot.GetComponentsInChildren<ToolMountPoint>(true) : System.Array.Empty<ToolMountPoint>();
    }

    bool EnsureMountRoot()
    {
        return EnsureMountRoot(false, out _);
    }

    bool EnsureMountRoot(bool refreshMountsIfChanged, out bool mountRootChanged)
    {
        mountRootChanged = false;

        if (!humanoidRigAnimator && transform.root)
            humanoidRigAnimator = transform.root.GetComponentInChildren<HumanoidRigAnimator>(true);

        var desiredMountRoot = humanoidRigAnimator ? humanoidRigAnimator.transform : transform.root;
        if (!desiredMountRoot || desiredMountRoot.root != transform.root)
            return false;

        if (mountRoot == desiredMountRoot && mountRoot && mountRoot.root == transform.root)
            return true;

        var previousMountRoot = mountRoot;
        mountRoot = desiredMountRoot;
        mountRootChanged = previousMountRoot != mountRoot;

        if (mountRootChanged && refreshMountsIfChanged)
            RefreshMountPoints();

        return true;
    }

    void CacheArmSolvers()
    {
        if (!mountRoot)
        {
            handIKSolvers.Clear();
            return;
        }

        var solvers = mountRoot.GetComponentsInChildren<FullBodyBipedIK>(true);
        var seenSides = new HashSet<HandMount.HandSide>();

        foreach (var solver in solvers)
        {
            CacheArmBinding(solver, solver.solver.leftHandEffector, HandMount.HandSide.Left, seenSides);
            CacheArmBinding(solver, solver.solver.rightHandEffector, HandMount.HandSide.Right, seenSides);
        }

        var toRemove = new List<HandMount.HandSide>();
        foreach (var entry in handIKSolvers)
        {
            if (!entry.Value.solver || entry.Value.effector == null || !seenSides.Contains(entry.Key))
                toRemove.Add(entry.Key);
        }

        for (int i = 0; i < toRemove.Count; i++)
            handIKSolvers.Remove(toRemove[i]);
    }

    void CacheArmBinding(FullBodyBipedIK solver, IKEffector effector, HandMount.HandSide side, HashSet<HandMount.HandSide> seenSides)
    {
        if (!solver || effector == null)
            return;

        seenSides.Add(side);

        var defaultTarget = effector.target;

        if (handIKSolvers.TryGetValue(side, out var binding))
        {
            if (binding.solver == solver && binding.effector == effector)
                return;

            if (binding.defaultTarget != null)
                defaultTarget = binding.defaultTarget;
        }

        handIKSolvers[side] = new ArmBinding
        {
            solver = solver,
            effector = effector,
            defaultTarget = defaultTarget,
        };
    }

    void UpdateHandTargetsFromEquippedInstance()
    {
        Transform leftPalm = null;
        Transform leftWrist = null;
        Transform rightPalm = null;
        Transform rightWrist = null;

        EnsureDefaultWristTargets();

        bool hasEquippedInstance = equippedInstance != null;

        if (hasEquippedInstance)
        {
            var mounts = equippedInstance.GetComponentsInChildren<HandMount>(true);
            foreach (var mount in mounts)
            {
                if (!mount)
                    continue;

                var mountTransform = mount.MountTransform;
                if (mountTransform)
                {
                    var poseAuthoring = mountTransform.GetComponent<HandMountPoseAuthoring>();
                    if (poseAuthoring != null)
                    {
                        poseAuthoring.ApplyOrientation();
                    }
                    else if (mountTransform != mount.transform)
                    {
                        var fallbackPose = mount.GetComponent<HandMountPoseAuthoring>();
                        if (fallbackPose != null)
                            fallbackPose.ApplyOrientation();
                    }
                }

                switch (mount.Hand)
                {
                    case HandMount.HandSide.Left:
                        if (mount.Part == HandMount.HandPart.Palm)
                        {
                            if (leftPalm == null)
                                leftPalm = mountTransform;
                        }
                        else if (mount.Part == HandMount.HandPart.Wrist)
                        {
                            if (leftWrist == null)
                                leftWrist = mountTransform;
                        }
                        break;
                    case HandMount.HandSide.Right:
                        if (mount.Part == HandMount.HandPart.Palm)
                        {
                            if (rightPalm == null)
                                rightPalm = mountTransform;
                        }
                        else if (mount.Part == HandMount.HandPart.Wrist)
                        {
                            if (rightWrist == null)
                                rightWrist = mountTransform;
                        }
                        break;
                }
            }
        }

        if (!hasEquippedInstance)
        {
            if (leftWrist == null)
                leftWrist = defaultLeftWristTarget;

            if (rightWrist == null)
                rightWrist = defaultRightWristTarget;
        }

        Transform leftFallback = leftPalm != null ? leftPalm : leftWrist;
        Transform rightFallback = rightPalm != null ? rightPalm : rightWrist;

        ApplyArmTarget(HandMount.HandSide.Left, leftFallback);
        ApplyArmTarget(HandMount.HandSide.Right, rightFallback);

        if (humanoidRigAnimator)
            humanoidRigAnimator.ApplyHandTargets(leftWrist, rightWrist, leftPalm, rightPalm);
    }

    void EnsureDefaultWristTargets()
    {
        if (!mountRoot)
            return;

        if (!defaultLeftWristTarget || !defaultRightWristTarget)
            TryResolveDefaultWristTargetsFromCharacterMounts();

        if (!defaultLeftWristTarget && !defaultLeftWristErrorLogged)
        {
            defaultLeftWristErrorLogged = true;
            Debug.LogError("Toolbelt: failed to resolve default left wrist target. Configure defaultLeftWristTarget or add a wrist HandMount on the character.", this);
        }

        if (!defaultRightWristTarget && !defaultRightWristErrorLogged)
        {
            defaultRightWristErrorLogged = true;
            Debug.LogError("Toolbelt: failed to resolve default right wrist target. Configure defaultRightWristTarget or add a wrist HandMount on the character.", this);
        }
    }

    void TryResolveDefaultWristTargetsFromCharacterMounts()
    {
        if (!mountRoot)
            return;

        foreach (var mount in mountRoot.GetComponentsInChildren<HandMount>(true))
        {
            if (!mount || mount.Part != HandMount.HandPart.Wrist)
                continue;

            var mountTransform = mount.MountTransform;
            if (!mountTransform)
                continue;

            if (equippedInstance && mountTransform.IsChildOf(equippedInstance.transform))
                continue;

            if (!defaultLeftWristTarget && mount.Hand == HandMount.HandSide.Left)
            {
                defaultLeftWristTarget = mountTransform;
            }
            else if (!defaultRightWristTarget && mount.Hand == HandMount.HandSide.Right)
            {
                defaultRightWristTarget = mountTransform;
            }

            if (defaultLeftWristTarget && defaultRightWristTarget)
                break;
        }
    }

    void ApplyArmTarget(HandMount.HandSide side, Transform target)
    {
        if (!handIKSolvers.TryGetValue(side, out var binding))
            return;

        var solver = binding.solver;
        var effector = binding.effector;
        if (!solver || effector == null)
            return;

        var resolved = target != null ? target : binding.defaultTarget;
        if (effector.target == resolved)
            return;

        effector.target = resolved;

       // if (!Application.isPlaying || solver.isActiveAndEnabled)
       //     solver.solve();
    }

    void ResetArmTargets()
    {
        foreach (var entry in handIKSolvers)
            ApplyArmTarget(entry.Key, null);

        if (humanoidRigAnimator)
            humanoidRigAnimator.ApplyHandTargets(null, null, null, null);
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
                {
                    Debug.Log($"Toolbelt: {itemName} using away mount for missing {stance} stance", this);
                    return target;
                }
            }

            target = FindMountPoint(ToolMountPoint.MountType.Fallback, stance);
            if (target)
            {
                Debug.Log($"Toolbelt: {itemName} using fallback type for {stance} stance", this);
                return target;
            }
        }

        Transform fallbackAway = FindMountPoint(ToolMountPoint.MountType.Fallback, ToolMountPoint.MountStance.Away);
        if (fallbackAway)
        {
            Debug.Log($"Toolbelt: {itemName} using fallback away mount", this);
            return fallbackAway;
        }

        Debug.LogWarning($"Toolbelt: defaulting {itemName} to mount root for {stance} stance", this);
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

    int DetermineRegistryIndexForSlot(int oneBasedSlot, ItemDefinition item)
    {
        if (!item)
            return -1;

        int idx = IndexOf(item);
        if (idx < 0)
            return -1;

        if (!DefinitionMatchesSlot(oneBasedSlot, item))
        {
            Debug.LogWarning($"Toolbelt: {item.name} cannot be assigned to slot {oneBasedSlot} (expected {ExpectedCategoryForSlot(oneBasedSlot)})", this);
            return -1;
        }

        return idx;
    }

    bool TryGetRegistryIndexForSlot(int oneBasedSlot, ItemDefinition item, out int registryIndex)
    {
        registryIndex = -1;
        if (!item)
            return false;

        registryIndex = IndexOf(item);
        if (registryIndex < 0)
            return false;

        if (!DefinitionMatchesSlot(oneBasedSlot, item))
        {
            Debug.LogWarning($"Toolbelt: {item.name} cannot be assigned to slot {oneBasedSlot} (expected {ExpectedCategoryForSlot(oneBasedSlot)})", this);
            registryIndex = -1;
            return false;
        }

        return true;
    }

    int DetermineInitialEquippedSlot()
    {
        foreach (var slot in EnumerateSlots())
        {
            if (slot.RegistryIndex >= 0)
                return (int)slot.Slot;
        }

        return SlotCount;
    }

    ItemDefinition GetRegistryDefinition(int registryIndex)
    {
        if (itemRegistry == null)
            return null;

        if (registryIndex < 0 || registryIndex >= itemRegistry.Count)
            return null;

        return itemRegistry[registryIndex];
    }

    int IndexOf(ItemDefinition def) => (def && itemRegistry != null) ? itemRegistry.IndexOf(def) : -1;

    ToolbeltSlotType ExpectedCategoryForSlot(int oneBasedSlot)
    {
        return oneBasedSlot switch
        {
            1 => ToolbeltSlotType.Primary,
            2 => ToolbeltSlotType.Secondary,
            3 => ToolbeltSlotType.Tertiary,
            4 => ToolbeltSlotType.Consumable,
            _ => ToolbeltSlotType.None,
        };
    }

    ToolbeltSlotType GetCategory(ItemDefinition def)
    {
        if (!def)
            return ToolbeltSlotType.None;
        return GetCategory(def.prefab);
    }

    ToolbeltSlotType GetCategory(GameObject prefab)
    {
        if (!prefab)
            return ToolbeltSlotType.None;

        var provider = FindCategoryProvider(prefab);
        return provider != null ? provider.ToolbeltCategory : ToolbeltSlotType.None;
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

    bool DefinitionMatchesSlot(int oneBasedSlot, ItemDefinition def)
    {
        if (!def)
            return false;

        ToolbeltSlotType expected = ExpectedCategoryForSlot(oneBasedSlot);
        if (expected == ToolbeltSlotType.None)
            return false;

        ToolbeltSlotType actual = GetCategory(def);
        return actual == expected;
    }

    public int CurrentSlot => equippedSlot;
    public ItemDefinition CurrentItem => GetSlot(equippedSlot);
    public ToolbeltSlotType CurrentCategory => GetCategory(CurrentItem);
    public GameObject CurrentEquippedObject => equippedInstance;
    public ToolMountPoint.MountStance EquippedStance => equippedStance;
    public KineticProjectileWeapon EquippedWeapon => equippedWeapon;
    public bool IsEquippedReady => !isProcessingRequest && equipRequestQueue.Count == 0;
    public KineticProjectileWeapon ActiveWeapon => (equippedStance == ToolMountPoint.MountStance.Active && IsEquippedReady) ? equippedWeapon : null;
    public bool HasActiveWeapon => ActiveWeapon != null;
    public int CurrentRequestSlot => activeRequestState;
    public int NextRequestSlot
    {
        get
        {
            if (equipRequestQueue.Count == 0)
                return -1;

            if (processingFromQueue && equipRequestQueue.Count > 1)
                return equipRequestQueue[1];

            if (!processingFromQueue)
                return equipRequestQueue[0];

            return -1;
        }
    }

    public ToolMountPoint.MountStance GetSlotStance(int oneBasedSlot)
    {
        var slot = GetSlotState(oneBasedSlot);
        if (slot == null || !slot.HasInstance)
            return ToolMountPoint.MountStance.Away;

        return slot.CurrentStance;
    }

    int GetRegistryIndex(int oneBasedSlot)
    {
        var slot = GetSlotState(oneBasedSlot);
        return slot?.RegistryIndex ?? -1;
    }

    public void RequestFireProjectile(KineticProjectileWeapon weapon, Vector3 origin, Vector3 dir, float speed, float damage)
    {
        if (!IsOwner)
            return;
        if (!weapon)
            return;
        if (!IsEquippedReady)
            return;

        int slot = equippedSlot;
        int registryIndex = GetRegistryIndex(slot);
        if (registryIndex < 0)
            return;

        RPC_FireEquippedProjectile(slot, registryIndex, origin, dir, speed, damage);
    }

    public void NotifyEquippedWeaponReloadState(KineticProjectileWeapon weapon, bool isReloading)
    {
        if (!weapon)
            return;

        bool hasTrackedWeapon = reloadingWeapon != null;
        bool matchesTrackedWeapon = hasTrackedWeapon && reloadingWeapon == weapon;
        bool hasEquippedWeapon = equippedWeapon != null;
        bool matchesEquippedWeapon = hasEquippedWeapon && equippedWeapon == weapon;

        if (isReloading)
        {
            if (!matchesEquippedWeapon)
            {
                if (hasTrackedWeapon && !matchesTrackedWeapon)
                    return;

                if (!hasTrackedWeapon && hasEquippedWeapon)
                    return;
            }

            if (equippedWeaponReloading && matchesTrackedWeapon)
                return;

            reloadingWeapon = weapon;
            equippedWeaponReloading = true;
            reloadStanceEndsAt = Time.time + Mathf.Max(0f, weapon.ReloadDuration);

            if (equippedStance != ToolMountPoint.MountStance.Reloading)
                stanceBeforeReloading = equippedStance;

            SetEquippedStance(ToolMountPoint.MountStance.Reloading);
            return;
        }

        if (hasTrackedWeapon)
        {
            if (!matchesTrackedWeapon)
                return;
        }
        else if (hasEquippedWeapon)
        {
            if (!matchesEquippedWeapon)
                return;
        }
        else
        {
            matchesTrackedWeapon = true;
        }

        bool shouldRestore = matchesEquippedWeapon || (!hasEquippedWeapon && matchesTrackedWeapon);
        if (!shouldRestore)
            return;

        CompleteReloadingStance();
    }

    void CompleteReloadingStance()
    {
        reloadStanceEndsAt = 0f;
        reloadingWeapon = null;
        equippedWeaponReloading = false;

        var desired = SanitizeEquippedStance(desiredEquippedStance);

        if (desired == ToolMountPoint.MountStance.Reloading)
            desired = DetermineMotorDrivenStance();

        if (desired == ToolMountPoint.MountStance.Reloading)
            desired = ToolMountPoint.MountStance.Passive;

        SetEquippedStance(desired);
        stanceBeforeReloading = desired;
    }

    [ServerRpc]
    void RPC_FireEquippedProjectile(int slot, int registryIndex, Vector3 origin, Vector3 dir, float speed, float damage)
    {
        var state = GetSlotState(slot);
        if (state == null)
            return;
        if (state.RegistryIndex != registryIndex)
            return;

        var def = GetSlot(slot);
        if (def?.prefab == null)
            return;

        var weapon = def.prefab.GetComponent<KineticProjectileWeapon>();
        if (!weapon)
            return;

        Vector3 muzzleOrigin = origin;
        Vector3 rayOrigin = origin + dir * 0.15f;
        float projectileRange = weapon.ProjectileRange;
        if (projectileRange <= 0f && speed > 0f)
            projectileRange = speed * 3f;
        float maxDistance = Mathf.Max(0.01f, projectileRange);
        float radius = weapon.ProjectileRadius;
        int mask = weapon.ProjectileHitMask.value != 0 ? weapon.ProjectileHitMask.value : Physics.DefaultRaycastLayers;

        var hits = Physics.SphereCastAll(rayOrigin, radius, dir, maxDistance, mask, QueryTriggerInteraction.Ignore);
        if (hits.Length > 1)
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        var shooterRoot = transform.root;
        var shooterIdentity = shooterRoot ? shooterRoot.GetComponent<NetworkObject>() : null;

        Vector3 impactPoint = muzzleOrigin + dir * maxDistance;
        Vector3 impactNormal = -dir;
        bool hitSomething = false;

        foreach (var hit in hits)
        {
            if (!hit.transform)
                continue;

            if (shooterRoot && hit.transform.IsChildOf(shooterRoot))
                continue;

            var shootable = hit.collider.GetComponentInParent<IShootable>();
            if (shootable != null)
            {
                if (shooterRoot && shootable.OwnerRoot == shooterRoot)
                    continue;

                impactPoint = hit.point;
                impactNormal = hit.normal;
                hitSomething = true;

                if (shootable.CanBeShot(shooterIdentity, hit.point, hit.normal))
                    shootable.ServerOnShot(shooterIdentity, damage, hit.point, hit.normal);

                break;
            }

            impactPoint = hit.point;
            impactNormal = hit.normal;
            hitSomething = true;
            break;
        }

        RPC_PlayEquippedFireFeedback(slot, registryIndex, muzzleOrigin, impactPoint, impactNormal, hitSomething);
    }

    [ObserversRpc]
    void RPC_PlayEquippedFireFeedback(int slot, int registryIndex, Vector3 origin, Vector3 endPoint, Vector3 hitNormal, bool hitSomething)
    {
        var state = GetSlotState(slot);
        if (state == null || state.RegistryIndex != registryIndex)
            return;

        bool suppressLocalFeedback = IsOwner && !IsServer;
        equippedWeapon?.OnServerFired(origin, endPoint, hitNormal, hitSomething, suppressLocalFeedback);
    }

    void LateUpdate()
    {
        int safety = 0;
        while (isProcessingRequest && Time.time >= equipTransitionEndsAt && safety < 8)
        {
            CompleteProcessingState();
            safety++;
        }

        if ((IsOwner || IsServer) && !isProcessingRequest)
            ProcessQueue();

        bool needsRebuild = false;
        bool mountRootChanged = false;

        if (IsClient)
        {
            if (!EnsureMountRoot(true, out mountRootChanged))
            {
                if (equippedInstance)
                    ClearVisuals();

                return;
            }
        }

        if (!IsServer)
            needsRebuild |= UpdateSlotsFromSyncVars();

        if (IsClient && !IsOwner)
            needsRebuild |= ApplyEquippedFromSyncVar();

        if (IsClient && !IsOwner)
            needsRebuild |= ApplyEquippedStanceFromSyncVar();

        needsRebuild |= mountRootChanged;

        bool allowVisuals = ShouldRenderVisuals;
        if (!allowVisuals)
        {
            if (equippedInstance)
                ClearVisuals();
        }
        else
        {
            if (needsRebuild && IsClient)
                RebuildVisual(equippedSlot);

            if (IsClient)
            {
                float now = Time.time;
                foreach (var slot in EnumerateSlots())
                    slot?.UpdateVisual(now);
            }
        }

        if (equippedWeaponReloading && reloadStanceEndsAt > 0f && Time.time >= reloadStanceEndsAt)
            ResolveReloadTimeout();
    }

    public IReadOnlyList<ItemDefinition> ItemRegistry => itemRegistry;

    private bool ShouldRenderVisuals => renderVisuals && (!renderVisualsIfOwner || IsOwner);

    public bool VisualsEnabled
    {
        get => renderVisuals;
        set
        {
            if (renderVisuals == value)
                return;

            renderVisuals = value;

            if (!ShouldRenderVisuals)
            {
                ClearVisuals();
            }
            else if (IsClient)
            {
                RebuildVisual(equippedSlot);
            }
        }
    }

    public ToolbeltSnapshot CaptureSnapshot()
    {
        return new ToolbeltSnapshot
        {
            Slot0 = primarySlot?.RegistryIndex ?? -1,
            Slot1 = secondarySlot?.RegistryIndex ?? -1,
            Slot2 = tertiarySlot?.RegistryIndex ?? -1,
            Slot3 = consumableSlot?.RegistryIndex ?? -1,
            EquippedSlot = Mathf.Clamp(equippedSlot, 1, SlotCount),
            EquippedStance = ResolveEquippedStance(),
        };
    }

    public void ApplySnapshot(in ToolbeltSnapshot snapshot, bool rebuildVisual = true)
    {
        if (primarySlot != null)
            primarySlot.RegistryIndex = snapshot.Slot0;

        if (secondarySlot != null)
            secondarySlot.RegistryIndex = snapshot.Slot1;

        if (tertiarySlot != null)
            tertiarySlot.RegistryIndex = snapshot.Slot2;

        if (consumableSlot != null)
            consumableSlot.RegistryIndex = snapshot.Slot3;

        equippedSlot = Mathf.Clamp(snapshot.EquippedSlot, 1, SlotCount);
        ApplyEquippedStanceInternal(snapshot.EquippedStance, refreshVisual: false, updateDesired: false);

        if (rebuildVisual && IsClient && ShouldRenderVisuals)
            RebuildVisual(equippedSlot);
    }

    void ResolveReloadTimeout()
    {
        reloadStanceEndsAt = 0f;

        var weapon = reloadingWeapon ? reloadingWeapon : equippedWeapon;
        if (!weapon)
        {
            CompleteReloadingStance();
            return;
        }

        NotifyEquippedWeaponReloadState(weapon, false);
    }

    void ServerStoreSlotIndex(int oneBasedSlot, int value)
    {
        switch (oneBasedSlot)
        {
            case 1: slot0Net.Value = value; break;
            case 2: slot1Net.Value = value; break;
            case 3: slot2Net.Value = value; break;
            case 4: slot3Net.Value = value; break;
        }
    }

    void ServerSyncEquipped(int slot)
    {
        equippedSlotNet.Value = Mathf.Clamp(slot, 1, SlotCount);
    }

    void ServerSyncEquippedStance(ToolMountPoint.MountStance stance)
    {
        equippedStanceNet.Value = (int)SanitizeEquippedStance(stance);
    }

    bool UpdateSlotsFromSyncVars()
    {
        bool currentChanged = false;
        int currentIndex = Mathf.Clamp(equippedSlot, 1, SlotCount);

        if (slot0Net.Value != primarySlot.RegistryIndex)
        {
            primarySlot.RegistryIndex = slot0Net.Value;
            currentChanged |= currentIndex == 1;
        }

        if (slot1Net.Value != secondarySlot.RegistryIndex)
        {
            secondarySlot.RegistryIndex = slot1Net.Value;
            currentChanged |= currentIndex == 2;
        }

        if (slot2Net.Value != tertiarySlot.RegistryIndex)
        {
            tertiarySlot.RegistryIndex = slot2Net.Value;
            currentChanged |= currentIndex == 3;
        }

        if (slot3Net.Value != consumableSlot.RegistryIndex)
        {
            consumableSlot.RegistryIndex = slot3Net.Value;
            currentChanged |= currentIndex == 4;
        }

        return currentChanged;
    }

    bool ApplyEquippedFromSyncVar()
    {
        int desired = Mathf.Clamp(equippedSlotNet.Value, 1, SlotCount);
        if (desired != equippedSlot)
        {
            StartEquipAnimationInternal(desired, false, false);
            return true;
        }
        return false;
    }

    bool ApplyEquippedStanceFromSyncVar()
    {
        var desired = (ToolMountPoint.MountStance)equippedStanceNet.Value;
        var sanitized = SanitizeEquippedStance(desired);
        if (equippedStance != sanitized)
        {
            ApplyEquippedStanceInternal(sanitized, false);
            return true;
        }
        return false;
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

    void SetSlotRegistryIndex(ToolBeltSlot slot, int registryIndex)
    {
        if (slot == null)
            return;

        slot.RegistryIndex = registryIndex;
    }

    internal bool IsCategoryEquipped(ToolbeltSlotType tertiary)
    {
        throw new NotImplementedException();
    }
    ToolMountPoint.MountStance ResolveEquippedStance()
    {
        return equippedStance switch
        {
            ToolMountPoint.MountStance.Active => ToolMountPoint.MountStance.Active,
            ToolMountPoint.MountStance.Passive => ToolMountPoint.MountStance.Passive,
            ToolMountPoint.MountStance.Away => ToolMountPoint.MountStance.Away,
            ToolMountPoint.MountStance.Reloading => ToolMountPoint.MountStance.Reloading,
            _ => ToolMountPoint.MountStance.Passive,
        };
    }

    ToolMountPoint.MountStance SanitizeEquippedStance(ToolMountPoint.MountStance stance)
    {
        return stance switch
        {
            ToolMountPoint.MountStance.Active => ToolMountPoint.MountStance.Active,
            ToolMountPoint.MountStance.Passive => ToolMountPoint.MountStance.Passive,
            ToolMountPoint.MountStance.Away => ToolMountPoint.MountStance.Away,
            ToolMountPoint.MountStance.Reloading => ToolMountPoint.MountStance.Reloading,
            _ => ToolMountPoint.MountStance.Passive,
        };
    }

    void ApplyEquippedStanceInternal(ToolMountPoint.MountStance stance, bool refreshVisual = true, bool updateDesired = true)
    {
        var sanitized = SanitizeEquippedStance(stance);
        if (equippedStance == sanitized && !refreshVisual)
            return;

        var previous = equippedStance;
        equippedStance = sanitized;

        if (previous == ToolMountPoint.MountStance.Reloading && sanitized != ToolMountPoint.MountStance.Reloading)
        {
            reloadingWeapon = null;
            equippedWeaponReloading = false;
            reloadStanceEndsAt = 0f;
        }

        if (updateDesired)
        {
            desiredEquippedStance = sanitized;
            desiredStanceDirty = false;
        }

        if (refreshVisual && IsClient)
            ApplyEquippedVisual(equippedSlot);
    }

    [ServerRpc]
    void RPC_RequestEquippedStance(int stanceValue)
    {
        var stance = (ToolMountPoint.MountStance)stanceValue;
        ApplyEquippedStanceInternal(stance);
        ServerSyncEquippedStance(stance);
        RPC_SetEquippedStance(stanceValue);
    }

    [ObserversRpc]
    void RPC_SetEquippedStance(int stanceValue)
    {
        ApplyEquippedStanceInternal((ToolMountPoint.MountStance)stanceValue);
    }

    void UpdateEquippedWeaponReference()
    {
        if (!equippedInstance)
        {
            equippedWeapon = null;
            return;
        }

        equippedWeapon = equippedInstance.GetComponentInChildren<KineticProjectileWeapon>(true);
    }

    public void SetEquippedStance(ToolMountPoint.MountStance stance)
    {
        var sanitized = SanitizeEquippedStance(stance);
        desiredEquippedStance = sanitized;
        desiredStanceDirty = true;

        bool allowDuringTransition = sanitized == ToolMountPoint.MountStance.Away
            || !isProcessingRequest
            || (isProcessingRequest && activeRequestState > 0);

        if (!allowDuringTransition)
            return;

        bool refreshVisual = sanitized == ToolMountPoint.MountStance.Away
            || !isProcessingRequest;

        ApplyDesiredStance(allowDuringTransition, refreshVisual);
    }

    void ApplyDesiredStance(bool allowDuringTransition, bool refreshVisual)
    {
        var sanitized = SanitizeEquippedStance(desiredEquippedStance);
        bool needsApply = desiredStanceDirty || equippedStance != sanitized;
        if (!needsApply)
        {
            desiredStanceDirty = false;
            return;
        }

        if (!allowDuringTransition && sanitized != ToolMountPoint.MountStance.Away && isProcessingRequest)
            return;

        desiredEquippedStance = sanitized;
        desiredStanceDirty = false;

        bool wasDifferent = equippedStance != sanitized;

        ApplyEquippedStanceInternal(sanitized, refreshVisual, updateDesired: false);

        if (wasDifferent)
            ReplicateEquippedStance(sanitized);
    }

    void ForceEquippedStance(ToolMountPoint.MountStance stance)
    {
        var sanitized = SanitizeEquippedStance(stance);
        bool changed = equippedStance != sanitized;

        ApplyEquippedStanceInternal(sanitized, true, updateDesired: false);

        if (desiredEquippedStance != sanitized)
            desiredStanceDirty = true;

        if (changed)
            ReplicateEquippedStance(sanitized);
    }

    void ReplicateEquippedStance(ToolMountPoint.MountStance stance)
    {
        if (IsServer)
        {
            ServerSyncEquippedStance(stance);
            RPC_SetEquippedStance((int)stance);
        }
        else if (IsOwner)
        {
            RPC_RequestEquippedStance((int)stance);
        }
    }

    void AttachStanceSource()
    {
        if (!stanceSource && transform.root)
            stanceSource = transform.root.GetComponentInChildren<TopDownMotor>(true);

        if (!stanceSource)
            return;

        stanceSource.StanceChanged -= HandleMotorStanceChanged;
        stanceSource.StanceChanged += HandleMotorStanceChanged;
        HandleMotorStanceChanged(stanceSource.CurrentStance);
    }

    void DetachStanceSource()
    {
        if (!stanceSource)
            return;

        stanceSource.StanceChanged -= HandleMotorStanceChanged;
    }

    void HandleMotorStanceChanged(TopDownMotor.Stance stance)
    {
        if (equippedStance == ToolMountPoint.MountStance.Away
            || equippedStance == ToolMountPoint.MountStance.Reloading)
            return;

        var desired = stance == TopDownMotor.Stance.Active
            ? ToolMountPoint.MountStance.Active
            : ToolMountPoint.MountStance.Passive;

        SetEquippedStance(desired);
    }

    ToolMountPoint.MountStance DetermineMotorDrivenStance()
    {
        if (!stanceSource && transform.root)
            stanceSource = transform.root.GetComponentInChildren<TopDownMotor>(true);

        var motor = stanceSource;
        if (!motor)
            return ToolMountPoint.MountStance.Passive;

        return motor.CurrentStance == TopDownMotor.Stance.Active
            ? ToolMountPoint.MountStance.Active
            : ToolMountPoint.MountStance.Passive;
    }

    void DebugLog(string message)
    {
        if (!enableEquipDebugLogs)
            return;

        Debug.Log($"[Toolbelt] {message}", this);
    }

}

