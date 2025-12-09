using UnityEngine;
using Assets.Scripts.Items.Weapons;
using FishNet.Object;

public class BlockShootable : MonoBehaviour, IShootable
{
    [SerializeField] MineableBlock block;
    [SerializeField] Transform ownerRootOverride; // usually null

    public Transform OwnerRoot => ownerRootOverride ? ownerRootOverride : transform.root;

    void Awake()
    {
        if (!block) block = GetComponentInParent<MineableBlock>(true);
    }

    public bool CanBeShot(NetworkObject shooter, Vector3 point, Vector3 normal) => block != null;

    public void ServerOnShot(NetworkObject shooter, float damage, float force, Vector3 point, Vector3 normal)
    {
        if (!block)
            return;

        block.ReportHit(Mathf.RoundToInt(damage));
        // optional: pass shooter if you want credit/aggro
    }
}
