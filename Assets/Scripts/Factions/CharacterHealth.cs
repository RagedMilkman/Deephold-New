using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using RootMotion.FinalIK;
using RootMotion.Dynamics;
using UnityEngine;
using UnityEngine.Serialization;

public enum LifeState { Alive = 0, Dead = 1 }

/// <summary>
/// Handles health and damage routing for a character with multiple hitboxes.
/// Server is authoritative for health/damage. PuppetMaster simulation runs only on:
/// - Owner client (for player characters), and
/// - Server for unowned objects (NPCs / server-owned).
/// Non-owner clients never run PuppetMaster; they only receive FX and state.
/// </summary>
public class CharacterHealth : NetworkBehaviour
{
    [Header("Health")]
    [FormerlySerializedAs("maxHealth")]
    [SerializeField] int _maxHealth = 30;
    [FormerlySerializedAs("despawnOnDeath")]
    [SerializeField] bool _despawnOnDeath = true;
    [FormerlySerializedAs("despawnDelay")]
    [SerializeField] float _despawnDelay = 2f;
    [FormerlySerializedAs("characterCollider")]
    [SerializeField, Tooltip("Primary collider used for movement (disabled on death).")]
    Collider _characterCollider;

    [SerializeField] Transform _ownerRoot;
    [SerializeField] List<HitBox> _hitBoxes = new();
    [SerializeField, Tooltip("Maximum localized health applied to each body part.")]
    int _maxLocalizedHealth = 30;

    [Header("Hit FX")]
    [SerializeField, Tooltip("Optional FX spawners used for local and ghost visuals.")]
    List<BloodHitFxVisualizer> _bloodHitFxVisualizers = new();

    [SerializeField, Tooltip("Relays hit FX to a spawned ghost visualizer, if present.")]
    BoneSnapshotReplicator _boneSnapshotReplicator;

    [Header("PuppetMaster / Death")]
    [FormerlySerializedAs("puppetMaster")]
    [SerializeField, Tooltip("Optional PuppetMaster used for hit flinches and death ragdoll.")]
    PuppetMaster _puppetMaster;
    public PuppetMaster PuppetMaster => _puppetMaster;

    [SerializeField, Tooltip("Base state settings used when killing the PuppetMaster on death.")]
    PuppetMaster.StateSettings _deathStateSettings;

    [SerializeField, Tooltip("Animator controlling the character's poses.")]
    Animator _animator;

    [SerializeField, Tooltip("IK solvers to disable when transitioning to ragdoll.")]
    IK[] _ikSolvers;

    [SerializeField, Tooltip("Global multiplier for forces applied to PuppetMaster on lethal hits.")]
    float _puppetMasterForceMultiplier = 1f;

    [Header("Body Part Effects")]
    [SerializeField, Tooltip("Optional movement controller to slow when legs are damaged.")]
    TopDownMotor _topDownMotor;
    [SerializeField, Tooltip("Default IK position weight for leg LimbIK solvers (full health).")]
    float _legIkPositionWeight = 0.5f;
    [SerializeField, Tooltip("Default IK rotation weight for leg LimbIK solvers (full health).")]
    float _legIkRotationWeight = 0f;
    [SerializeField, Tooltip("Maximum IK rotation weight for leg LimbIK solvers when fully damaged.")]
    float _legIkMaxRotationWeight = 0.25f;

    [System.Serializable]
    public struct DeathProfile
    {
        [Tooltip("Blend time into ragdoll.")]
        public float killDuration;
        [Tooltip("Muscle weight once dead (0 = floppy, ~0.2 is a good middle ground).")]
        public float deadMuscleWeight;
        [Tooltip("Damping for dead muscles (higher = less jitter).")]
        public float deadMuscleDamper;
        [Tooltip("Extra multiplier on applied force for this death type.")]
        public float forceMultiplier;
    }

    [Header("Death Profiles")]
    [SerializeField, Tooltip("Used for headshots / instant kills.")]
    DeathProfile _instantDeath; // head

    [SerializeField, Tooltip("Used for torso shots.")]
    DeathProfile _quickDeath;   // torso

    [SerializeField, Tooltip("Used for limbs / everything else.")]
    DeathProfile _slowDeath;    // limbs / default

    [Header("Non-lethal hit flinch")]
    [SerializeField, Tooltip("How long PuppetMaster stays Active for a non-lethal hit flinch.")]
    float _hitImpulseDuration = 0.12f;

    [SerializeField, Tooltip("Delay before applying non-lethal hit force (allows PM to wake up).")]
    float _hitImpulseDelay = 0.01f;

    [SerializeField, Tooltip("Multiplier for non-lethal hit forces (flinch only).")]
    float _hitImpulseForceMultiplier = 0.2f;

    [SerializeField, Tooltip("Maximum non-lethal impulse magnitude applied to a muscle.")]
    float _hitImpulseMaxForce = 5f;

    [SerializeField, Tooltip("Delay before applying dead PuppetMaster weights (allows state to settle).")]
    float _deadWeightsDelay = 0.05f;

    [SerializeField, Tooltip("Additional damping applied once the character is dead.")]
    float _deadForceMultiplier = 0.25f;

    [SerializeField, Tooltip("Mapping weight applied to the PuppetMaster once dead.")]
    float _deadMappingWeight = 0.3f;

    [SerializeField, Tooltip("Pin weight applied to the PuppetMaster once dead.")]
    float _deadPinWeight = 0.3f;

    [SerializeField, Tooltip("Muscle weight applied to the PuppetMaster once dead.")]
    float _deadMuscleWeight = 0.3f;
    readonly SyncDictionary<BodyPart, int> _localizedHealth = new();
    LimbIK _leftLegIk;
    LimbIK _rightLegIk;

    // FX runtime state
    // Tracks which hitbox spawned the death blood pool so late joiners can
    // parent the effect to the same bone hierarchy across RPCs.
    [AllowMutableSyncType]
    readonly SyncVar<int> _bloodPoolHitBoxIndex = new(-1);

    // Runtime
    Coroutine _hitImpulseRoutine;

    public int Health { get; private set; }
    public int MaxHealth => _maxHealth;
    public LifeState State { get; private set; } = LifeState.Alive;

    public Transform OwnerRoot => _ownerRoot ? _ownerRoot : transform.root;
    public IReadOnlyList<HitBox> HitBoxes => _hitBoxes;
    public Animator Animator => _animator;

    void Awake()
    {
        if (!_ownerRoot) _ownerRoot = transform.root;
        if (!_topDownMotor) _topDownMotor = GetComponentInChildren<TopDownMotor>(true);
        if (!_puppetMaster) _puppetMaster = GetComponentInChildren<PuppetMaster>(true);
        if (!_animator) _animator = GetComponentInChildren<Animator>(true);
        if (_ikSolvers == null || _ikSolvers.Length == 0) _ikSolvers = GetComponentsInChildren<IK>(true);
        CacheBloodHitFxVisualizers();
        if (!_boneSnapshotReplicator) _boneSnapshotReplicator = GetComponent<BoneSnapshotReplicator>();
        if (!_characterCollider) _characterCollider = GetComponent<Collider>();

        CacheLegIkSolvers();

        RefreshHitBoxes();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyPuppetMasterRunnerState();
        ApplyColliderLifeState(State);
        if (State == LifeState.Dead)
        {
            ApplyPuppetMasterDeathState();
            var position = GetBloodPoolSpawnPosition(out Transform parent, out _);
            ForEachBloodFx(v => v.SpawnDeathBloodPool(position, parent));
        }
        ApplyBodyPartEffects();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        Health = _maxHealth;
        State = LifeState.Alive;
        _bloodPoolHitBoxIndex.Value = -1;
        ForEachBloodFx(v => v.ResetBloodPoolSpawn());
        ApplyColliderLifeState(State);
        InitializeLocalizedHealth();
        ApplyPuppetMasterRunnerState();
        RPC_State(Health, _maxHealth, (int)State);
    }

    void OnEnable()
    {
        // Helps when objects are pooled / re-enabled.
        _localizedHealth.OnChange += OnLocalizedHealthChanged;
        _bloodPoolHitBoxIndex.Value = -1;
        ForEachBloodFx(v => v.ResetBloodPoolSpawn());
        ApplyPuppetMasterRunnerState();
        ApplyBodyPartEffects();
    }

    void OnValidate()
    {
        CacheBloodHitFxVisualizers();
    }

    void OnDisable()
    {
        _localizedHealth.OnChange -= OnLocalizedHealthChanged;
    }

    bool ShouldRunPuppetMaster()
    {
        // Owner client runs it (host's local player included).
        if (IsClient && IsOwner)
            return true;

        // Server runs it only for unowned objects (NPCs / server-only).
        if (IsServer && !Owner.IsValid)
            return true;

        return false;
    }

    void ApplyPuppetMasterRunnerState()
    {
        if (_puppetMaster == null)
            return;

        bool run = ShouldRunPuppetMaster();

        // Non-runner instances should never tick PM.
        _puppetMaster.enabled = run;

        // Optional: keep the GameObject alive if you rely on references elsewhere.
        // If you prefer hard-off, keep the SetActive below.
        _puppetMaster.gameObject.SetActive(run);
    }

    public bool CanBeShot(NetworkObject shooter, Vector3 point, Vector3 normal)
    {
        return true; // State == LifeState.Alive;
    }

    public void RefreshHitBoxes()
    {
        _hitBoxes.Clear();
        foreach (var hitBox in GetComponentsInChildren<HitBox>(false))
        {
            hitBox.SetOwner(this);
            _hitBoxes.Add(hitBox);
        }
    }

    void InitializeLocalizedHealth()
    {
        if (!IsServer)
            return;

        _localizedHealth.Clear();

        foreach (BodyPart part in Enum.GetValues(typeof(BodyPart)))
        {
            _localizedHealth[part] = Mathf.Max(0, _maxLocalizedHealth);
        }
    }

    public int GetHitBoxIndex(HitBox hitBox)
    {
        if (hitBox == null)
            return -1;

        int index = _hitBoxes.IndexOf(hitBox);
        if (index < 0)
        {
            RefreshHitBoxes();
            index = _hitBoxes.IndexOf(hitBox);
        }

        return index;
    }

    Transform GetHitBoxTransform(int hitBoxIndex)
    {
        if (hitBoxIndex < 0 || hitBoxIndex >= _hitBoxes.Count)
            return null;

        return _hitBoxes[hitBoxIndex].transform;
    }

    void CacheLegIkSolvers()
    {
        if (_leftLegIk != null && _rightLegIk != null)
            return;

        foreach (var limbIk in GetComponentsInChildren<LimbIK>(true))
        {
            string nameLower = limbIk.name.ToLowerInvariant();

            if (_leftLegIk == null && nameLower.Contains("left") && nameLower.Contains("leg"))
            {
                _leftLegIk = limbIk;
            }
            else if (_rightLegIk == null && nameLower.Contains("right") && nameLower.Contains("leg"))
            {
                _rightLegIk = limbIk;
            }
        }
    }

    public void OnHit(
        BodyPart bodyPart,
        float damage,
        Vector3 hitPoint,
        Vector3 hitDir,
        float force,
        int puppetMasterMuscleIndex,
        int hitBoxIndex,
        NetworkObject shooter = null)
    {
        // Server authority: only server mutates health/state.
        if (!IsServer)
            return;

        var finalDamage = Mathf.RoundToInt(Mathf.Max(0f, damage));
        if (finalDamage <= 0)
            return;

        ApplyLocalizedDamage(bodyPart, finalDamage);

        // FX always replicated.
        RPC_PlayHitFx(hitPoint, -hitDir, hitDir, force, hitBoxIndex);

        bool wasAlive = State == LifeState.Alive;

        ServerDamage(finalDamage, shooter);

        // Lethal: alive -> dead
        if (wasAlive && State == LifeState.Dead)
        {
            // Replicate ragdoll activation + impulse to the PM runner(s).
            RPC_EnterDeathRagdoll((int)bodyPart, hitPoint, hitDir, force, puppetMasterMuscleIndex);
        }
        // Non-lethal: flinch on the PM runner(s).
        else if (wasAlive && State == LifeState.Alive)
        {
            RPC_NonLethalImpulse(hitPoint, hitDir, force, puppetMasterMuscleIndex);
        }
        // Already-dead ragdoll: impulse (and FX already handled above).
        else if (!wasAlive && State == LifeState.Dead)
        {
            RPC_DeadImpulse(hitPoint, hitDir, force, puppetMasterMuscleIndex);
        }
    }

    void ApplyLocalizedDamage(BodyPart bodyPart, int finalDamage)
    {
        int current = GetLocalizedHealth(bodyPart);
        int updated = Mathf.Max(0, current - finalDamage);

        _localizedHealth[bodyPart] = updated;

        if (State == LifeState.Alive && updated == 0)
        {
            ServerDamage(Health);
        }
    }

    int GetLocalizedHealth(BodyPart bodyPart)
    {
        if (_localizedHealth.TryGetValue(bodyPart, out int current))
            return current;

        return _maxLocalizedHealth;
    }

    float GetLocalizedHealth01(BodyPart bodyPart)
    {
        float max = Mathf.Max(1f, _maxLocalizedHealth);
        return Mathf.Clamp01(GetLocalizedHealth(bodyPart) / max);
    }

    void OnLocalizedHealthChanged(SyncDictionaryOperation op, BodyPart key, int value, bool asServer)
    {
        ApplyBodyPartEffects();
    }

    void ApplyBodyPartEffects()
    {
        float leftLegHealth = GetLocalizedHealth01(BodyPart.LegL);
        float rightLegHealth = GetLocalizedHealth01(BodyPart.LegR);
        float legHealthFactor = Mathf.Min(leftLegHealth, rightLegHealth);

        ApplyMovementPenalty(legHealthFactor);
        ApplyLegIkWeights(leftLegHealth, rightLegHealth);
    }

    void ApplyMovementPenalty(float legHealthFactor)
    {
        if (_topDownMotor == null)
            return;

        float multiplier = Mathf.Clamp01(legHealthFactor);
        _topDownMotor.SetExternalSpeedMultiplier(multiplier);
    }

    void ApplyLegIkWeights(float leftLegHealth, float rightLegHealth)
    {
        ApplyLegIkWeight(_leftLegIk, leftLegHealth);
        ApplyLegIkWeight(_rightLegIk, rightLegHealth);
    }

    void ApplyLegIkWeight(LimbIK limbIk, float normalizedHealth)
    {
        if (limbIk == null || limbIk.solver == null)
            return;

        float damageFactor = 1f - Mathf.Clamp01(normalizedHealth);
        limbIk.solver.IKPositionWeight = Mathf.Lerp(_legIkPositionWeight, 0f, damageFactor);
        limbIk.solver.IKRotationWeight = Mathf.Lerp(_legIkRotationWeight, _legIkMaxRotationWeight, damageFactor);
    }

    Transform GetMostDamagedHitBoxTransform(out int hitBoxIndex)
    {
        hitBoxIndex = -1;
        if (_hitBoxes == null || _hitBoxes.Count == 0)
            return null;

        Transform bestTransform = null;
        int lowestHealth = int.MaxValue;

        for (int i = 0; i < _hitBoxes.Count; i++)
        {
            var hitBox = _hitBoxes[i];
            if (hitBox == null)
                continue;

            int localizedHealth = GetLocalizedHealth(hitBox.BodyPart);
            if (localizedHealth < lowestHealth)
            {
                lowestHealth = localizedHealth;
                bestTransform = hitBox.transform;
                hitBoxIndex = i;
            }
        }

        return bestTransform;
    }

    Transform GetBloodPoolParent(out int hitBoxIndex)
    {
        if (_bloodPoolHitBoxIndex.Value >= 0)
        {
            hitBoxIndex = _bloodPoolHitBoxIndex.Value;
            return GetHitBoxTransform(_bloodPoolHitBoxIndex.Value);
        }

        return GetMostDamagedHitBoxTransform(out hitBoxIndex);
    }

    Vector3 GetBloodPoolSpawnPosition(out Transform parent, out int hitBoxIndex)
    {
        parent = GetBloodPoolParent(out hitBoxIndex);
        return parent != null ? parent.position : OwnerRoot.position;
    }

    [ObserversRpc]
    void RPC_EnterDeathRagdoll(int bodyPartInt, Vector3 hitPoint, Vector3 hitDir, float force, int muscleIndex)
    {
        if (!ShouldRunPuppetMaster())
            return;

        DisableAnimationSystems();

        DeathProfile profile;
        switch ((BodyPart)bodyPartInt)
        {
            case BodyPart.Head:
                profile = _instantDeath;
                break;
            case BodyPart.Torso:
                profile = _quickDeath;
                break;
            default:
                profile = _slowDeath;
                break;
        }

        ActivateDeathRagdoll(hitPoint, hitDir, force, muscleIndex, profile);
    }

    [ObserversRpc]
    void RPC_SpawnBloodPool(Vector3 position, int hitBoxIndex)
    {
        _bloodPoolHitBoxIndex.Value = hitBoxIndex;
        Transform parent = GetHitBoxTransform(hitBoxIndex);
        ForEachBloodFx(v => v.SpawnDeathBloodPool(position, parent));
    }

    [ObserversRpc]
    void RPC_NonLethalImpulse(Vector3 hitPoint, Vector3 hitDir, float force, int muscleIndex)
    {
        if (!ShouldRunPuppetMaster())
            return;

        ApplyNonLethalHitImpulse(hitPoint, hitDir, force, muscleIndex);
    }

    [ObserversRpc]
    void RPC_DeadImpulse(Vector3 hitPoint, Vector3 hitDir, float force, int muscleIndex)
    {
        if (!ShouldRunPuppetMaster())
            return;

        ApplyPuppetMasterImpulse(
            hitPoint,
            hitDir,
            force,
            muscleIndex,
            applyGlobalForceMultiplier: true,
            applyDeadForceReduction: true);
    }

    void ActivateDeathRagdoll(
        Vector3 hitPoint,
        Vector3 hitDir,
        float baseForce,
        int puppetMasterMuscleIndex,
        DeathProfile profile)
    {
        if (_puppetMaster == null)
            return;

        // Ensure PM is active on the runner.
        _puppetMaster.gameObject.SetActive(true);
        _puppetMaster.enabled = true;
        _puppetMaster.mode = PuppetMaster.Mode.Active;
        _puppetMaster.state = PuppetMaster.State.Alive;

        // Clone base settings and override per-profile fields.
        var killSettings = _deathStateSettings;
        killSettings.killDuration = profile.killDuration;
        killSettings.deadMuscleWeight = profile.deadMuscleWeight;
        killSettings.deadMuscleDamper = profile.deadMuscleDamper;

        _puppetMaster.Kill(killSettings);

        if (_deadWeightsDelay > 0f)
            StartCoroutine(ApplyDeadMasterWeightsDelayed());
        else
            ApplyDeadMasterWeights();

        // Apply impulse scaled by global and per-profile multipliers.
        ApplyPuppetMasterImpulse(
            hitPoint,
            hitDir,
            baseForce,
            puppetMasterMuscleIndex,
            profile.forceMultiplier,
            applyGlobalForceMultiplier: true,
            applyDeadForceReduction: true);
    }

    void DisableAnimationSystems()
    {
        if (_animator)
            _animator.enabled = false;

        if (_ikSolvers == null)
            return;

        for (int i = 0; i < _ikSolvers.Length; i++)
        {
            if (_ikSolvers[i] != null)
                _ikSolvers[i].enabled = false;
        }
    }

    void ApplyPuppetMasterImpulse(
        Vector3 hitPoint,
        Vector3 hitDir,
        float force,
        int puppetMasterMuscleIndex,
        float extraForceMultiplier = 1f,
        bool applyGlobalForceMultiplier = false,
        bool applyDeadForceReduction = false)
    {
        if (_puppetMaster == null)
            return;

        var muscles = _puppetMaster.muscles;
        if (muscles == null || muscles.Length == 0)
            return;

        float totalForce = force * extraForceMultiplier;
        if (applyGlobalForceMultiplier)
            totalForce *= _puppetMasterForceMultiplier;

        if (applyDeadForceReduction && State == LifeState.Dead)
            totalForce *= _deadForceMultiplier;

        if (totalForce <= 0f)
            return;

        var impactForce = hitDir.normalized * totalForce;
        var targetIndex = (puppetMasterMuscleIndex >= 0 && puppetMasterMuscleIndex < muscles.Length)
            ? puppetMasterMuscleIndex
            : 0;

        muscles[targetIndex].rigidbody.AddForceAtPosition(impactForce, hitPoint, ForceMode.Impulse);
    }

    void ApplyDeadMasterWeights()
    {
        if (_puppetMaster == null)
            return;

        _puppetMaster.mappingWeight = _deadMappingWeight;
        _puppetMaster.pinWeight = _deadPinWeight;
        _puppetMaster.muscleWeight = _deadMuscleWeight;
    }

    System.Collections.IEnumerator ApplyDeadMasterWeightsDelayed()
    {
        if (_deadWeightsDelay > 0f)
            yield return new WaitForSeconds(_deadWeightsDelay);

        ApplyDeadMasterWeights();
    }

    // -------- FX --------

    [ObserversRpc]
    void RPC_PlayHitFx(
        Vector3 hitPoint,
        Vector3 surfaceNormal,
        Vector3 hitDir,
        float force,
        int hitBoxIndex)
    {
        Transform spawnParent = GetHitBoxTransform(hitBoxIndex);
        if (spawnParent == null)
            spawnParent = OwnerRoot;

        ForEachBloodFx(v => v.PlayHitFx(hitPoint, surfaceNormal, spawnParent, force, hitDir));
        _boneSnapshotReplicator?.RelayHitFxToGhost(hitPoint, surfaceNormal, spawnParent, force, hitDir);
    }

    // -------- Non-lethal flinch --------

    void ApplyNonLethalHitImpulse(
        Vector3 hitPoint,
        Vector3 hitDir,
        float force,
        int puppetMasterMuscleIndex)
    {
        if (_puppetMaster == null)
            return;
        if (State != LifeState.Alive)
            return;

        if (_hitImpulseRoutine != null)
            StopCoroutine(_hitImpulseRoutine);

        _hitImpulseRoutine = StartCoroutine(
            HitImpulseRoutine(hitPoint, hitDir, force, puppetMasterMuscleIndex));
    }

    System.Collections.IEnumerator HitImpulseRoutine(
        Vector3 hitPoint,
        Vector3 hitDir,
        float force,
        int puppetMasterMuscleIndex)
    {
        if (_puppetMaster == null)
            yield break;

        var muscles = _puppetMaster.muscles;
        if (muscles == null || muscles.Length == 0)
            yield break;

        // Cache current mode (usually Kinematic while alive).
        var previousMode = _puppetMaster.mode;

        // Briefly go Active so the impulse actually moves the ragdoll.
        _puppetMaster.mode = PuppetMaster.Mode.Active;

        if (_hitImpulseDelay > 0f)
            yield return new WaitForSeconds(_hitImpulseDelay);

        // If they died during the delay, let death logic take over.
        if (State == LifeState.Dead)
            yield break;

        // Scale and clamp the flinch force so it can't knock them over.
        float clamped = Mathf.Min(force, _hitImpulseMaxForce);
        float flinchForceMag = clamped * _hitImpulseForceMultiplier;
        if (flinchForceMag <= 0f)
            yield break;

        Vector3 impactForce = hitDir.normalized * flinchForceMag;

        int targetIndex = (puppetMasterMuscleIndex >= 0 && puppetMasterMuscleIndex < muscles.Length)
            ? puppetMasterMuscleIndex
            : 0;

        muscles[targetIndex].rigidbody.AddForceAtPosition(impactForce, hitPoint, ForceMode.Impulse);

        // Keep Active for a very short time so the flinch is visible but can't fully topple them.
        yield return new WaitForSeconds(_hitImpulseDuration);

        // Don't override full ragdoll if they've died since.
        if (State == LifeState.Dead)
            yield break;

        _puppetMaster.mode = previousMode;
        _hitImpulseRoutine = null;
    }

    public void ServerDamage(int amount, NetworkObject attacker = null)
    {
        if (!IsServer || State == LifeState.Dead)
            return;

        Health = Mathf.Max(0, Health - Mathf.Abs(amount));
        if (Health == 0)
        {
            State = LifeState.Dead;
            ApplyPuppetMasterDeathState();
            ApplyColliderLifeState(State);
            Vector3 bloodPoolPosition = GetBloodPoolSpawnPosition(out Transform parent, out int hitBoxIndex);
            _bloodPoolHitBoxIndex.Value = hitBoxIndex;
            ForEachBloodFx(v => v.SpawnDeathBloodPool(bloodPoolPosition, parent));
            RPC_State(Health, _maxHealth, (int)State);
            RPC_SpawnBloodPool(bloodPoolPosition, hitBoxIndex);
            if (_despawnOnDeath) Invoke(nameof(ServerDespawn), Mathf.Max(0f, _despawnDelay));
        }
        else
        {
            ApplyColliderLifeState(LifeState.Alive);
            RPC_State(Health, _maxHealth, (int)LifeState.Alive);
        }
    }

    void ServerDespawn()
    {
        if (!IsServer)
            return;

        if (_puppetMaster)
        {
            var puppetRoot = _puppetMaster.transform.root.gameObject;
            if (puppetRoot != gameObject)
                Destroy(puppetRoot);
        }
        Destroy(gameObject);
    }

    [ObserversRpc]
    void RPC_State(int hp, int maxHp, int st)
    {
        Health = hp;
        _maxHealth = maxHp;
        State = (LifeState)st;
        ApplyColliderLifeState(State);
        if (State == LifeState.Dead)
        {
            ApplyPuppetMasterDeathState();
            var position = GetBloodPoolSpawnPosition(out Transform parent, out _);
            ForEachBloodFx(v => v.SpawnDeathBloodPool(position, parent));
        }
    }

    void ApplyPuppetMasterDeathState()
    {
        if (!_puppetMaster)
            return;

        bool run = ShouldRunPuppetMaster();

        _puppetMaster.gameObject.SetActive(run);
        _puppetMaster.enabled = run;

        if (!run)
            return;

        _puppetMaster.mode = PuppetMaster.Mode.Active;
        _puppetMaster.state = PuppetMaster.State.Dead;
    }

    void ApplyColliderLifeState(LifeState state)
    {
        if (!_characterCollider)
            return;

        _characterCollider.enabled = state != LifeState.Dead;
    }

    void CacheBloodHitFxVisualizers()
    {
        _bloodHitFxVisualizers.RemoveAll(v => v == null);
        foreach (var fx in GetComponentsInChildren<BloodHitFxVisualizer>(true))
        {
            if (!_bloodHitFxVisualizers.Contains(fx))
                _bloodHitFxVisualizers.Add(fx);
        }
    }

    void ForEachBloodFx(Action<BloodHitFxVisualizer> callback)
    {
        if (callback == null || _bloodHitFxVisualizers == null)
            return;

        for (int i = 0; i < _bloodHitFxVisualizers.Count; i++)
        {
            var fx = _bloodHitFxVisualizers[i];
            if (fx == null)
                continue;

            callback(fx);
        }
    }
}
