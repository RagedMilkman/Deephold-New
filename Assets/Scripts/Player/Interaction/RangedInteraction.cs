using UnityEngine;
using UnityEngine.InputSystem;

/// Drives whichever KineticProjectileWeapon the Toolbelt has in the active stance.
/// Works when tools change at runtime (equip/unequip).
public class RangedInteraction : PlayerInteraction
{
    [Header("Toolbelt source")]
    [SerializeField] ToolbeltNetworked toolbelt;
    [Tooltip("If true, only drive the weapon when the toolbelt stance is Active.")]
    [SerializeField] bool requireActiveWeapon = true;

    Camera ownerCam;
    KineticProjectileWeapon currentWeapon;

    protected override void Awake()
    {
        base.Awake();
        // allow multiple interactions on the same object
        requireOwner = true;
        requireAlive = true;
        allowOnServer = false;
    }

    protected override void OnInteractionSpawned(bool asServer)
    {
        // Resolve toolbelt
        if (!toolbelt || toolbelt.transform.root != transform.root)
        {
            toolbelt = transform.root.GetComponentInChildren<ToolbeltNetworked>(true);
            if (!toolbelt)
                Debug.LogWarning($"RangedInteraction: couldn't find Toolbelt on {transform.root.name}", this);
        }

        // Resolve camera once for the owner
        if (IsOwner)
        {
            ownerCam = GetComponentInChildren<Camera>(true);
            if (!ownerCam) ownerCam = Camera.main;
        }

        RefreshCurrentWeapon();
    }

    protected override void OnActiveUpdate()
    {
        RefreshCurrentWeapon();

        if (!currentWeapon) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        var kb = Keyboard.current;

        // trigger semantics depend on weapon mode
        bool triggerPressed = currentWeapon.IsAutomatic
            ? mouse.leftButton.isPressed
            : mouse.leftButton.wasPressedThisFrame;

        bool reloadPressed = kb != null && kb.rKey.wasPressedThisFrame;

        // drive the weapon
        currentWeapon.InteractionTick(triggerPressed, reloadPressed);
    }

    protected override void OnBecameDead()
    {
        // Drop reference so we don’t feed input while dead
        currentWeapon = null;
    }

    public void NotifyEquippedChanged() => RefreshCurrentWeapon();

    void RefreshCurrentWeapon()
    {
        KineticProjectileWeapon nextWeapon = null;

        if (toolbelt)
        {
            nextWeapon = toolbelt.ActiveWeapon;

            if (!nextWeapon && !requireActiveWeapon)
                nextWeapon = toolbelt.EquippedWeapon;
        }

        if (ReferenceEquals(nextWeapon, currentWeapon))
            return;

        currentWeapon = nextWeapon;

        if (currentWeapon)
        {
            // hand it the camera & ensure it's interaction-driven
            currentWeapon.InteractionSetCamera(ownerCam);
        }
    }
}
