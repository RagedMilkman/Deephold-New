using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

public class PickaxeTool : NetworkBehaviour, IPlayerTool, IToolbeltItemCategoryProvider
{
    [Header("Melee Cast")]
    [SerializeField] Transform toolMount;            // assign Player/Character/ToolMount
    [SerializeField] float backProbe = 0.35f;        // how far to check behind first
    [SerializeField] float range = 0.5f;             // reach
    [SerializeField] float radius = 0.25f;           // forgiving hit volume
    [SerializeField] LayerMask blockMask;            // Blocks (or Default)

    [Header("Mining")]
    [SerializeField] int damagePerHit = 1;
    [SerializeField] float hitCooldown = 0.35f;

    [Header("Control")]
    [Tooltip("When true, this tool expects a MeleeInteraction to call InteractionTick().")]
    [SerializeField] bool driveByInteraction = true;

    [Header("Toolbelt")]
    [SerializeField] private ToolMountPoint.MountType toolbeltMountType = ToolMountPoint.MountType.LargeMeleeWeapon;

    [Header("Equipping")]
    [SerializeField, Min(0f)] private float equipDuration = 0.25f;
    [SerializeField, Min(0f)] private float unequipDuration = 0.2f;
    [SerializeField, Min(0f)] private float stanceTransitionDuration = 0.1f;

    NetworkObject ownerIdentity;
    Camera ownerCam;                // not used right now, kept for parity with tools/weapons
    float nextHitTime;

    public ToolbeltSlotType ToolbeltCategory => ToolbeltSlotType.Primary;
    public ToolMountPoint.MountType ToolbeltMountType => toolbeltMountType;
    public float ToolbeltEquipDuration => Mathf.Max(0f, equipDuration);
    public float ToolbeltUnequipDuration => Mathf.Max(0f, unequipDuration);
    public float ToolbeltStanceTransitionDuration => Mathf.Max(0f, stanceTransitionDuration);

    void Awake()
    {
        ownerIdentity = transform.root.GetComponent<NetworkObject>();
        if (!toolMount) toolMount = FindToolMount(transform.root);
    }

    void OnEnable()
    {
        if (!toolMount) toolMount = FindToolMount(transform.root);
    }

    // Standalone input path (only if NOT driven by interaction)
    void Update()
    {
        if (driveByInteraction) return;

        if (ownerIdentity == null || !ownerIdentity.IsOwner) return;
        if (Mouse.current?.leftButton.wasPressedThisFrame == true)
            TryMine();
    }

    // ---------- IPlayerTool ----------
    public void InteractionSetCamera(Camera cam) => ownerCam = cam;

    // primaryPressed: LMB edge, primaryHeld: LMB held (you can expand later)
    public void InteractionTick(bool primaryHeld, bool primaryPressed,
                                bool secondaryHeld, bool secondaryPressed)
    {
        if (ownerIdentity == null || !ownerIdentity.IsOwner) return;
        if (primaryPressed) TryMine();
    }
    // ---------------------------------

    public void TryMine()
    {
        if (Time.time < nextHitTime) return;
        nextHitTime = Time.time + hitCooldown;

        if (!toolMount) toolMount = FindToolMount(transform.root);
        if (!toolMount) return;

        Vector3 fwd = toolMount.forward;
        Vector3 origin = toolMount.position - fwd * backProbe;     // start a bit behind
        float dist = backProbe + range;                        // cover full arc

        if (CastForwardOnce(origin, fwd, dist, out RaycastHit hit))
        {
            var nid = hit.collider.GetComponentInParent<NetworkObject>();
            if (nid != null)
                MineBlock(nid, hit.point);
        }
    }

    // single forward cast with a self-skip safeguard
    bool CastForwardOnce(Vector3 origin, Vector3 dir, float distance, out RaycastHit hit)
    {
        int mask = (blockMask.value == 0) ? ~0 : blockMask.value;

        if (Physics.SphereCast(origin, radius, dir, out hit, distance, mask, QueryTriggerInteraction.Ignore))
        {
            // if we hit ourselves, nudge past and try once more
            if (hit.collider && hit.collider.transform.root == transform.root)
            {
                Vector3 newOrigin = hit.point + dir * 0.01f;
                return Physics.SphereCast(newOrigin, radius, dir, out hit, distance, mask, QueryTriggerInteraction.Ignore);
            }
            return true;
        }
        return false;
    }

    void MineBlock(NetworkObject targetIdentity, Vector3 hitPoint)
    {
        if (targetIdentity == null) return;

        var block = targetIdentity.GetComponent<MineableBlock>();
        if (!block) return;

        // simple sanity check: player to hit point distance (server-side)
        float maxServerRange = range + 0.5f;
        if (Vector3.Distance(transform.root.position, hitPoint) > maxServerRange)
            return;

        block.ReportHit(damagePerHit);
    }

    // --- helpers ---
    Transform FindToolMount(Transform root)
    {
        var t = root.Find("Character/ToolMount");
        if (t) return t;

        foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            if (tr.name == "ToolMount") return tr;

        return null;
    }
}
