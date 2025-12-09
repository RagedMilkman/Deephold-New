using FishNet.Object;
using RootMotion.Dynamics;
using RootMotion.FinalIK;
using UnityEngine;

public enum LifeState { Alive = 0, Dead = 1 }

public enum DeathType
{
    Head = 0,
    Torso = 1,
    Limb = 2
}

[System.Serializable]
public class PuppetMasterDeathSettings
{
    public bool activateObject = true;
    public bool enableComponent = true;
    public PuppetMaster.Mode mode = PuppetMaster.Mode.Active;
    public PuppetMaster.State state = PuppetMaster.State.Dead;
}

[System.Serializable]
public class LimbIkDeathSettings
{
    public bool enable = false;
    [Range(0f, 1f)] public float positionWeight = 0f;
    [Range(0f, 1f)] public float rotationWeight = 0f;
}

[System.Serializable]
public class PuppetMasterDeathConfig
{
    public DeathType deathType = DeathType.Torso;
    public PuppetMasterDeathSettings puppetMaster = new();
    public LimbIkDeathSettings limbIk = new();
}

public class CharacterState : NetworkBehaviour
{
    [SerializeField] int maxHealth = 30;
    [SerializeField] bool despawnOnDeath = true;
    [SerializeField] float despawnDelay = 2f;
    [SerializeField, Tooltip("Optional PuppetMaster to activate on death.")] PuppetMaster puppetMaster;
    [SerializeField, Tooltip("Death configs that control PuppetMaster and LimbIK behaviour.")]
    PuppetMasterDeathConfig[] deathConfigs;

    LimbIK[] limbIkComponents;

    public int Health { get; private set; }
    public int MaxHealth => maxHealth;
    public LifeState State { get; private set; } = LifeState.Alive;
    public DeathType LastDeathType { get; private set; } = DeathType.Torso;

    void Awake()
    {
        if (!puppetMaster)
            puppetMaster = GetComponentInChildren<PuppetMaster>(true);

        limbIkComponents = GetComponentsInChildren<LimbIK>(true);
    }

    public override void OnStartServer()
    {
        if (IsServer)
        {
            Health = maxHealth;
            State = LifeState.Alive;
            RPC_State(Health, maxHealth, (int)State, (int)LastDeathType);
        }
    }

    public void ServerDamage(int amount, NetworkObject attacker = null, BodyPart bodyPart = BodyPart.Torso)
    {
        if (!IsServer || State == LifeState.Dead) return;

        Health = Mathf.Max(0, Health - Mathf.Abs(amount));
        if (Health == 0)
        {
            State = LifeState.Dead;
            LastDeathType = GetDeathType(bodyPart);
            ApplyPuppetMasterDeathState(LastDeathType);
            RPC_State(Health, maxHealth, (int)State, (int)LastDeathType);
            if (despawnOnDeath) Invoke(nameof(ServerDespawn), Mathf.Max(0f, despawnDelay));
        }
        else
        {
            RPC_State(Health, maxHealth, (int)LifeState.Alive, (int)LastDeathType);
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
    void RPC_State(int hp, int maxHp, int st, int deathType)
    {
        Health = hp; maxHealth = maxHp; State = (LifeState)st; LastDeathType = (DeathType)deathType;
        if (State == LifeState.Dead)
            ApplyPuppetMasterDeathState(LastDeathType);
    }

    void ApplyPuppetMasterDeathState(DeathType deathType)
    {
        ApplyPuppetSettings(deathType);
        ApplyLimbIkSettings(deathType);
    }

    void ApplyPuppetSettings(DeathType deathType)
    {
        if (!puppetMaster)
            return;

        var settings = ResolveDeathConfig(deathType).puppetMaster;

        puppetMaster.gameObject.SetActive(settings.activateObject);
        puppetMaster.enabled = settings.enableComponent;
        puppetMaster.mode = settings.mode;
        puppetMaster.state = settings.state;
    }

    void ApplyLimbIkSettings(DeathType deathType)
    {
        if (limbIkComponents == null || limbIkComponents.Length == 0)
            return;

        var settings = ResolveDeathConfig(deathType).limbIk;

        foreach (var limbIk in limbIkComponents)
        {
            if (limbIk == null)
                continue;

            limbIk.enabled = settings.enable;
            if (limbIk.solver != null)
            {
                limbIk.solver.IKPositionWeight = settings.positionWeight;
                limbIk.solver.IKRotationWeight = settings.rotationWeight;
            }
        }
    }

    PuppetMasterDeathConfig ResolveDeathConfig(DeathType deathType)
    {
        if (deathConfigs != null)
        {
            foreach (var config in deathConfigs)
            {
                if (config != null && config.deathType == deathType)
                    return config;
            }
        }

        return new PuppetMasterDeathConfig
        {
            deathType = deathType
        };
    }

    static DeathType GetDeathType(BodyPart bodyPart)
    {
        return bodyPart switch
        {
            BodyPart.Head => DeathType.Head,
            BodyPart.Torso => DeathType.Torso,
            _ => DeathType.Limb
        };
    }
}
