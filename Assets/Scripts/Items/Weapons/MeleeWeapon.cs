using Assets.Scripts.Items.Weapons;
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

    [Header("Animation")]
    [SerializeField] private ProceduralMeleeSwing swingAnimation;

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
    private static readonly RaycastHit[] castHits = new RaycastHit[8];

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
        if (!swingAnimation) swingAnimation = GetComponentInChildren<ProceduralMeleeSwing>(true);
    }

    protected virtual void OnEnable()
    {
        if (!swingOrigin) swingOrigin = transform;
        if (!swingAnimation) swingAnimation = GetComponentInChildren<ProceduralMeleeSwing>(true);
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

        if (swingAnimation)
            swingAnimation.Play();

        Transform castOrigin = swingOrigin ? swingOrigin : transform;
        Vector3 forward = ownerCam ? ownerCam.transform.forward : castOrigin.forward;
        Vector3 origin = castOrigin.position - forward * backProbe;
        float distance = backProbe + range;

        if (CastForward(origin, forward, distance, out RaycastHit hit))
            OnMeleeHit(hit);
    }

    protected virtual void OnMeleeHit(RaycastHit hit)
    {
        var shootable = hit.collider.GetComponentInParent<IShootable>();
        if (shootable != null && shootable.OwnerRoot != transform.root)
        {
            var shooter = ownerIdentity;
            if (shootable.CanBeShot(shooter, hit.point, hit.normal))
                shootable.ServerOnShot(shooter, damage, damage, hit.point, hit.normal);
            return;
        }

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
        int hitCount = Physics.SphereCastNonAlloc(origin, radius, dir, castHits, distance, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            var candidate = castHits[i];
            if (!candidate.collider || candidate.collider.transform.root == transform.root)
                continue;

            hit = candidate;
            return true;
        }

        hit = default;
        return false;
    }

    private bool IsLocalOwner()
    {
        return ownerIdentity != null && ownerIdentity.IsOwner;
    }
}
