using UnityEngine;
using Assets.Scripts.Items.Weapons;
using FishNet.Object;

public class ProjectileBullet : NetworkBehaviour
{
    [Header("Tuning")]
    [SerializeField] float lifeSeconds = 3f;
    [SerializeField] float radius = 0.06f;        // "thickness" of the bullet
    [SerializeField] LayerMask hitMask;           // Blocks (and later Enemies)

    // runtime state (server only)
    Vector3 dir;          // normalized
    float speed;          // units/sec
    float dieAt;
    Transform shooterRoot;
    bool inited;

    // Called from the weapon's server RPC
    public void ServerInit(Vector3 direction, float speedUnitsPerSec, float dmg, float force, Transform shooter)
    {
        if (!IsServer) return;

        dir = direction.normalized;
        speed = speedUnitsPerSec;
        shooterRoot = shooter;   // used to ignore self
        damage = dmg;
        impactForce = force;

        transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        dieAt = Time.time + lifeSeconds;
        inited = true;
    }

    float damage;
    float impactForce;

    void FixedUpdate()
    {
        if (!IsServer || !inited)
            return;

        if (Time.time >= dieAt) 
        { 
            Destroy(gameObject); 
            return; 
        }

        float step = speed * Time.fixedDeltaTime;
        Vector3 origin = transform.position;

        int mask = (hitMask.value == 0) ? ~0 : hitMask.value;
        if (Physics.SphereCast(origin, radius, dir, out var hit, step, mask, QueryTriggerInteraction.Ignore))
        {
            var shootable = hit.collider.GetComponentInParent<IShootable>();
            if (shootable != null)
            {
                // ignore shooting yourself
                if (shooterRoot && shootable.OwnerRoot == shooterRoot)
                {
                    transform.position = origin + dir * 0.02f; // nudge past
                    return;
                }

                var shooterId = shooterRoot ? shooterRoot.GetComponent<NetworkObject>() : null;


                Debug.Log("FixedUpdate");

                if (shootable.CanBeShot(shooterId, hit.point, hit.normal))
                    shootable.ServerOnShot(shooterId, damage, impactForce, hit.point, hit.normal);

                Destroy(gameObject);
                return;
            }

            // Unknown surface -> die (or decal later)
            Destroy(gameObject);
            return;
        }

        transform.position = origin + dir * step;
    }
}
