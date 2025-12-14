using FishNet.Object;
using UnityEngine;
using RootMotion.Dynamics;

public enum LifeState { Alive = 0, Dead = 1 }

public class CharacterState : NetworkBehaviour
{
    [SerializeField] int maxHealth = 30;
    [SerializeField] bool despawnOnDeath = true;
    [SerializeField] float despawnDelay = 2f;
    [SerializeField, Tooltip("Optional PuppetMaster to activate on death.")] PuppetMaster puppetMaster;
    [SerializeField, Tooltip("Master weights (mapping/pin/muscle) applied to the PuppetMaster once dead.")] float deadMasterWeight = 0.3f;

    public int Health { get; private set; }
    public int MaxHealth => maxHealth;
    public LifeState State { get; private set; } = LifeState.Alive;

    void Awake()
    {
        if (!puppetMaster)
            puppetMaster = GetComponentInChildren<PuppetMaster>(true);
    }

    public override void OnStartServer()
    {
        if (IsServer)
        {
            Health = maxHealth;
            State = LifeState.Alive;
            RPC_State(Health, maxHealth, (int)State);
        }
    }

    public override void OnStartClient()
    {
        if (!puppetMaster)
            return;

        bool run = ShouldRunPuppetMaster();
        puppetMaster.gameObject.SetActive(run);
        puppetMaster.enabled = run;
    }

    public void ServerDamage(int amount, NetworkObject attacker = null)
    {
        if (!IsServer || State == LifeState.Dead) return;

        Health = Mathf.Max(0, Health - Mathf.Abs(amount));
        if (Health == 0)
        {
            State = LifeState.Dead;
            ApplyPuppetMasterDeathState();
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

        if (puppetMaster)
        {
            var puppetRoot = puppetMaster.transform.root.gameObject;
            if (puppetRoot != gameObject)
                Destroy(puppetRoot);
        }
        Destroy(gameObject); // or your PurrNet despawn
    }

    [ObserversRpc]
    void RPC_State(int hp, int maxHp, int st)
    {
        Health = hp; maxHealth = maxHp; State = (LifeState)st;
        if (State == LifeState.Dead)
            ApplyPuppetMasterDeathState();
    }

    void ApplyPuppetMasterDeathState()
    {
        if (!puppetMaster)
            return;

        bool run = ShouldRunPuppetMaster();

        puppetMaster.gameObject.SetActive(run);
        puppetMaster.enabled = run;

        if (!run)
            return;

        puppetMaster.mode = PuppetMaster.Mode.Active;
        puppetMaster.state = PuppetMaster.State.Dead;
        puppetMaster.mappingWeight = deadMasterWeight;
        puppetMaster.pinWeight = deadMasterWeight;
        puppetMaster.muscleWeight = deadMasterWeight;
    }

    private bool ShouldRunPuppetMaster()
    {
        // Owner client runs it (host's local player included).
        if (IsClient && IsOwner)
            return true;

        // Server runs it only for unowned objects (NPCs / server-only).
        if (IsServer && !Owner.IsValid)
            return true;

        return false; // non-owner clients + server-side player objects
    }
}
