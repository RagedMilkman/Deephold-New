// MeleeInteractionFromMount.cs
using UnityEngine;
using UnityEngine.InputSystem;

public class MeleeInteraction : PlayerInteraction
{
    [Header("Where to look for the tool")]
    [SerializeField] Transform toolMount;
    [SerializeField] string mountPath = "Character/ToolMount";

    [Header("Optional references")]
    [SerializeField] ToolbeltNetworked toolbelt;

    [Header("Find strategy")]
    [SerializeField] bool requireActiveTool = true;   // ignore disabled tools

    Camera ownerCam;
    IPlayerTool currentTool;
    int lastChildCount = -1;
    float nextRescanAt = 0f;

    protected override void Awake()
    {
        base.Awake();
        requireOwner = true;
        requireAlive = true;
        allowOnServer = false;
    }

    protected override void OnInteractionSpawned(bool asServer)
    {
        if (!toolMount || toolMount.root != transform.root)
        {
            toolMount = transform.root.Find(mountPath);
            if (!toolMount)
                Debug.LogWarning($"MeleeInteraction: can't find mount at {transform.root.name}/{mountPath}", this);
        }

        if (!toolbelt)
        {
            toolbelt = GetComponent<ToolbeltNetworked>();
            if (!toolbelt && transform.root)
                toolbelt = transform.root.GetComponentInChildren<ToolbeltNetworked>(true);
        }

        if (isOwner)
        {
            ownerCam = GetComponentInChildren<Camera>(true);
            if (!ownerCam) ownerCam = Camera.main;
        }

        ForceRescan();
    }

    protected override void OnActiveUpdate()
    {
        // detect equips/unequips
        if (toolMount)
        {
            if (toolMount.childCount != lastChildCount || Time.time >= nextRescanAt || currentTool == null)
                RescanForTool();
        }

        if (currentTool == null) return;

        if (toolbelt && !toolbelt.IsCategoryEquipped(ToolbeltSlotType.Tertiary))
            return;

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
    public void NotifyEquippedChanged() => ForceRescan();

    // ---------- internals ----------
    void ForceRescan()
    {
        lastChildCount = -1;
        nextRescanAt = 0f;
        RescanForTool();
    }

    void RescanForTool()
    {
        lastChildCount = toolMount ? toolMount.childCount : -1;
        nextRescanAt = Time.time + 0.25f;

        IPlayerTool found = FindToolUnder(toolMount);

        if (!ReferenceEquals(found, currentTool))
        {
            currentTool = found;
            if (currentTool != null)
                currentTool.InteractionSetCamera(ownerCam);
        }
    }

    IPlayerTool FindToolUnder(Transform mount)
    {
        if (!mount) return null;

        // Prefer direct children
        for (int i = 0; i < mount.childCount; i++)
        {
            var child = mount.GetChild(i);
            if (!child) continue;

            var tool = GetUsableTool(child);
            if (tool != null) return tool;
        }

        // Fallback: any descendant
        foreach (var t in mount.GetComponentsInChildren<Transform>(true))
        {
            var tool = GetUsableTool(t);
            if (tool != null) return tool;
        }

        return null;
    }

    IPlayerTool GetUsableTool(Transform t)
    {
        if (!t) return null;
        var tool = t.GetComponent<IPlayerTool>();
        if (tool == null) return null;

        if (requireActiveTool)
        {
            var mb = tool as MonoBehaviour;
            if (mb != null && !mb.isActiveAndEnabled) return null;
        }

        // same player root?
        if (t.root != transform.root) return null;

        return tool;
    }
}
