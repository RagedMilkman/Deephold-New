using FishNet.Object;
using UnityEngine;

public enum LifeState { Alive = 0, Dead = 1 }

public class CharacterState : NetworkBehaviour
{
    [SerializeField] int maxHealth = 30;
    [SerializeField] bool despawnOnDeath = true;
    [SerializeField] float despawnDelay = 2f;

    public int Health { get; private set; }
    public int MaxHealth => maxHealth;
    public LifeState State { get; private set; } = LifeState.Alive;

    public override void OnStartServer()
    {
        if (IsServer)
        {
            Health = maxHealth;
            State = LifeState.Alive;
            RPC_State(Health, maxHealth, (int)State);
        }
    }

    public void ServerDamage(int amount, NetworkObject attacker = null)
    {
        if (!IsServer || State == LifeState.Dead) return;

        Health = Mathf.Max(0, Health - Mathf.Abs(amount));
        if (Health == 0)
        {
            State = LifeState.Dead;
            RPC_State(Health, maxHealth, (int)State);
            if (despawnOnDeath) Invoke(nameof(ServerDespawn), Mathf.Max(0f, despawnDelay));
        }
        else
        {
            RPC_State(Health, maxHealth, (int)LifeState.Alive);
        }
    }

    void ServerDespawn()
    {
        if (!IsServer) return;
        Destroy(gameObject); // or your PurrNet despawn
    }

    [ObserversRpc]
    void RPC_State(int hp, int maxHp, int st)
    {
        Health = hp; maxHealth = maxHp; State = (LifeState)st;
    }
}
