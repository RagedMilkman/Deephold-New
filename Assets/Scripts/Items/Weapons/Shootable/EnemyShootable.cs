using UnityEngine;
using Assets.Scripts.Items.Weapons;
using FishNet.Object;

public class EnemyShootable : MonoBehaviour, IShootable
{
    [SerializeField] CharacterState state;
    [SerializeField] Transform ownerRoot;          // set to enemy root (optional)
    [SerializeField] bool ignoreSameRoot = true;   // prevent self/team-self hits if desired

    public Transform OwnerRoot => ownerRoot ? ownerRoot : transform.root;

    void Awake()
    {
        if (!state) state = GetComponentInParent<CharacterState>(true);
        if (!ownerRoot) ownerRoot = state ? state.transform : transform.root;
    }

    public bool CanBeShot(NetworkObject shooter, Vector3 point, Vector3 normal)
    {
        if (!state || state.State == LifeState.Dead) return false;

        if (ignoreSameRoot && shooter != null)
        {
            var shooterRoot = shooter.transform ? shooter.transform.root : null;
            if (shooterRoot != null && shooterRoot == OwnerRoot)
                return false;
        }

        // TODO: team/faction, invuln windows, headshot masks, etc.

        return true;
    }

    public void ServerOnShot(NetworkObject shooter, float damage, Vector3 point, Vector3 normal)
    {
        if (!state) 
            return;

        Debug.Log("ServerOnShot!");

        state.ServerDamage(Mathf.RoundToInt(Mathf.Max(0f, damage)), shooter, BodyPart.Torso);
        // Optional: trigger observers FX here later
    }
}
