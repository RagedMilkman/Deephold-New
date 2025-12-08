using UnityEngine;
using UnityEngine.InputSystem;

/// Owner-only, alive-only input wrapper for your existing ToolbeltNetworked.
/// Keeps ToolbeltNetworked exactly as-is (no code changes needed).
public class ToolbeltInteraction : PlayerInteraction
{
    [SerializeField] ToolbeltNetworked toolbelt;

    int lastRequestedSlot = ToolbeltNetworked.SlotCount;

    protected override void Awake()
    {
        base.Awake();
        EnsureToolbeltAssigned();
        // This script is just input; ensure gating happens here:
        requireOwner = true;
        requireAlive = true;
        allowOnServer = false;

        CacheInitialSlot();
    }

    protected override void OnInteractionSpawned(bool asServer)
    {
        base.OnInteractionSpawned(asServer);
        EnsureToolbeltAssigned();
        CacheInitialSlot();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        EnsureToolbeltAssigned();
        CacheInitialSlot();
    }

    protected override void OnActiveUpdate()
    {
        EnsureToolbeltAssigned();
        if (!toolbelt) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        int requestedSlot = 0;
        if (kb.digit1Key.wasPressedThisFrame) requestedSlot = 1;
        else if (kb.digit2Key.wasPressedThisFrame) requestedSlot = 2;
        else if (kb.digit3Key.wasPressedThisFrame) requestedSlot = 3;
        else if (kb.digit4Key.wasPressedThisFrame) requestedSlot = 4;

        if (requestedSlot != 0)
        {
            lastRequestedSlot = requestedSlot;
            toolbelt.RequestEquip(requestedSlot);
        }
        else
        {
            MaintainLastSelection();
        }
    }

    protected override void OnBecameDead() { /* input already gated; no-op */ }
    protected override void OnBecameAlive()
    {
        CacheInitialSlot();
    }

    void CacheInitialSlot()
    {
        if (!toolbelt) return;
        int slot = toolbelt.CurrentSlot;
        if (slot < 1 || slot > ToolbeltNetworked.SlotCount) return;
        lastRequestedSlot = slot;
    }

    void MaintainLastSelection()
    {
        if (!toolbelt) return;
        int current = toolbelt.CurrentSlot;
        if (current < 1 || current > ToolbeltNetworked.SlotCount) return;
        if (current == lastRequestedSlot) return;
        toolbelt.RequestEquip(lastRequestedSlot);
    }

    // Safety catch for PlayerInput scroll bindings that might still fire.
    public void OnScrollWheel(InputValue _)
    {
        MaintainLastSelection();
    }

    void EnsureToolbeltAssigned()
    {
        if (toolbelt) return;
        toolbelt = GetComponent<ToolbeltNetworked>();
        if (!toolbelt)
            toolbelt = GetComponentInChildren<ToolbeltNetworked>(true);
    }
}
