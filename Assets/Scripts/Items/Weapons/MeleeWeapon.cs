using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Base class for melee weapons driven by interactions or standalone input.
/// </summary>
public abstract class MeleeWeapon : NetworkBehaviour, IPlayerTool, IToolbeltItemCategoryProvider, IWeapon
{
    [Header("Melee Cast")]
    [SerializeField] private Transform swingOrigin;
    [SerializeField, Min(0f)] private float backProbe = 0.3f;
    [SerializeField, Min(0f)] private float range = 1f;
    [SerializeField, Min(0f)] private float radius = 0.35f;
    [SerializeField] private LayerMask hitMask;

    [Header("Damage")]
    [SerializeField, Min(0f)] private float damage = 1f;
    [SerializeField] private WeaponRange weaponRange = new WeaponRange(0f, 0.5f, 1f, 1.5f, 2f);

    [Header("Control")]
    [Tooltip("When true, this weapon expects a MeleeInteraction to call InteractionTick().")]
    [SerializeField] private bool driveByInteraction = true;

    [Header("Toolbelt")]
    [SerializeField] private ToolMountPoint.MountType toolbeltMountType = ToolMountPoint.MountType.SmallMeleeWeapon;

    [Header("Equipping")]
    [SerializeField, Min(0f)] private float equipDuration = 0.2f;
    [SerializeField, Min(0f)] private float unequipDuration = 0.2f;
    [SerializeField, Min(0f)] private float stanceTransitionDuration = 0.1f;

    private NetworkObject ownerIdentity;
    private Camera ownerCam;
    private float nextSwingTime;

    public virtual ToolbeltSlotType ToolbeltCategory => ToolbeltSlotType.Secondary;
    public virtual ToolMountPoint.MountType ToolbeltMountType => toolbeltMountType;
    public float ToolbeltEquipDuration => Mathf.Max(0f, equipDuration);
    public float ToolbeltUnequipDuration => Mathf.Max(0f, unequipDuration);
    public float ToolbeltStanceTransitionDuration => Mathf.Max(0f, stanceTransitionDuration);
    public WeaponRange WeaponRange => weaponRange;

    protected virtual void Awake()
    {
        ownerIdentity = transform.root.GetComponent<NetworkObject>();
        if (!swingOrigin) swingOrigin = transform;
    }

    protected virtual void OnEnable()
    {
        if (!swingOrigin) swingOrigin = transform;
    }

    protected virtual void Update()
    {
        if (driveByInteraction)
            return;

        if (!IsLocalOwner())
            return;

        if (Mouse.current?.leftButton.wasPressedThisFrame == true)
            TrySwing();
    }

    public void InteractionSetCamera(Camera cam)
    {
        ownerCam = cam;
    }

    public void InteractionTick(bool primaryHeld, bool primaryPressed, bool secondaryHeld, bool secondaryPressed)
    {
        if (!IsLocalOwner())
            return;

        if (primaryPressed)
            TrySwing();
    }

    protected virtual void TrySwing()
    {
        if (Time.time < nextSwingTime)
            return;

        nextSwingTime = Time.time + Mathf.Max(0f, stanceTransitionDuration);

        Transform castOrigin = swingOrigin ? swingOrigin : transform;
        Vector3 forward = ownerCam ? ownerCam.transform.forward : castOrigin.forward;
        Vector3 origin = castOrigin.position - forward * backProbe;
        float distance = backProbe + range;

        if (CastForward(origin, forward, distance, out RaycastHit hit))
            OnMeleeHit(hit);
    }

    protected virtual void OnMeleeHit(RaycastHit hit)
    {
        // Hook for derived classes. Base implementation applies simple impact logic.
        var rigidbody = hit.rigidbody;
        if (rigidbody)
        {
            var forward = ownerCam ? ownerCam.transform.forward : (swingOrigin ? swingOrigin.forward : transform.forward);
            rigidbody.AddForceAtPosition(forward * damage, hit.point, ForceMode.Impulse);
        }
    }

    protected bool CastForward(Vector3 origin, Vector3 dir, float distance, out RaycastHit hit)
    {
        int mask = (hitMask.value == 0) ? ~0 : hitMask.value;

        if (Physics.SphereCast(origin, radius, dir, out hit, distance, mask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider && hit.collider.transform.root == transform.root)
            {
                Vector3 newOrigin = hit.point + dir * 0.01f;
                return Physics.SphereCast(newOrigin, radius, dir, out hit, distance, mask, QueryTriggerInteraction.Ignore);
            }

            return true;
        }

        return false;
    }

    private bool IsLocalOwner()
    {
        return ownerIdentity != null && ownerIdentity.IsOwner;
    }
}
