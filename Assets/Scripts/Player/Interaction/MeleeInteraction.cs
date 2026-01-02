using UnityEngine;
using UnityEngine.InputSystem;

public class MeleeInteraction : PlayerInteraction
{
    [Header("Toolbelt source")]
    [SerializeField] ToolbeltNetworked toolbelt;
    [Tooltip("If true, only drive the tool when the toolbelt stance is Active.")]
    [SerializeField] bool requireActiveStance = true;
    [Tooltip("If true, wait for the equipped item to finish swapping before driving it.")]
    [SerializeField] bool requireEquippedReady = true;

    [Header("Tool selection")]
    [SerializeField] bool requireActiveTool = true;   // ignore disabled tools

    Camera ownerCam;
    IPlayerTool currentTool;

    protected override void Awake()
    {
        base.Awake();
        requireOwner = true;
        requireAlive = true;
        allowOnServer = false;
    }

    protected override void OnInteractionSpawned(bool asServer)
    {
        EnsureToolbeltAssigned();

        if (IsOwner)
        {
            ownerCam = GetComponentInChildren<Camera>(true);
            if (!ownerCam) ownerCam = Camera.main;
        }

        RefreshCurrentTool();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        EnsureToolbeltAssigned();
        RefreshCurrentTool();
    }

    protected override void OnActiveUpdate()
    {
        RefreshCurrentTool();

        if (currentTool == null) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        bool pHeld = mouse.leftButton.isPressed;
        bool pEdge = mouse.leftButton.wasPressedThisFrame;
        bool sHeld = mouse.rightButton.isPressed;
        bool sEdge = mouse.rightButton.wasPressedThisFrame;

        currentTool.InteractionTick(pHeld, pEdge, sHeld, sEdge);
    }

    protected override void OnBecameDead()
    {
        currentTool = null; // drop ref so nothing is driven while dead
    }

    // Optional hook for your Toolbelt to call after equip
    public void NotifyEquippedChanged() => RefreshCurrentTool();

    // ---------- internals ----------
    void EnsureToolbeltAssigned()
    {
        if (toolbelt && toolbelt.transform.root == transform.root)
            return;

        toolbelt = transform.root.GetComponentInChildren<ToolbeltNetworked>(true);
        if (!toolbelt)
            Debug.LogWarning($"MeleeInteraction: couldn't find Toolbelt on {transform.root.name}", this);
    }

    void RefreshCurrentTool()
    {
        IPlayerTool nextTool = null;

        if (toolbelt && (!requireActiveStance || toolbelt.EquippedStance == ToolMountPoint.MountStance.Active))
        {
            if (!requireEquippedReady || toolbelt.IsEquippedReady)
            {
                var equipped = toolbelt.CurrentEquippedObject;
                if (equipped)
                    nextTool = equipped.GetComponentInChildren<IPlayerTool>(true);
            }
        }

        if (requireActiveTool && nextTool is MonoBehaviour mb && !mb.isActiveAndEnabled)
            nextTool = null;

        if (ReferenceEquals(nextTool, currentTool))
            return;

        currentTool = nextTool;
        if (currentTool != null)
            currentTool.InteractionSetCamera(ownerCam);
    }
}
