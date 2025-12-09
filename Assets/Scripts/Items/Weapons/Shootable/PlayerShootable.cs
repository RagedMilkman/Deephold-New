using UnityEngine;
using Assets.Scripts.Items.Weapons;
using FishNet.Object;

public class PlayerShootable : MonoBehaviour, IShootable
{
    [SerializeField] CharacterState state;
    [SerializeField] Transform ownerRoot; // set to player root

    public Transform OwnerRoot => ownerRoot ? ownerRoot : transform.root;

    void Awake()
    {
        if (!state) state = GetComponentInParent<CharacterState>(true);
        if (!ownerRoot) ownerRoot = state ? state.transform : transform.root;
    }

    public bool CanBeShot(NetworkObject shooter, Vector3 point, Vector3 normal)
    {
        // dead? invulnerable? team check? do it here.
        return state && state.State == LifeState.Alive;
    }

    public void ServerOnShot(NetworkObject shooter, float damage, Vector3 point, Vector3 normal)
    {
        if (!state || !state.IsServer) 
            return;

        Debug.Log("SHOT");

        state.ServerDamage(Mathf.RoundToInt(damage), shooter, BodyPart.Torso);
        // FX hooks (server -> ObserversRpc) can live here later
    }
}
