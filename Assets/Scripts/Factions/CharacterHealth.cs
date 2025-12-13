using System.Collections.Generic;
using FishNet.Object;
using RootMotion.FinalIK;
using RootMotion.Dynamics;
using UnityEngine;

/// <summary>
/// Handles health and damage routing for a character with multiple hitboxes.
/// </summary>
public class CharacterHealth : NetworkBehaviour
{
    [SerializeField] CharacterState _state;
    [SerializeField] Transform _ownerRoot;
    [SerializeField] List<HitBox> _hitBoxes = new();

    [Header("Hit FX")]
    [SerializeField, Tooltip("Optional FX spawner used for local and ghost visuals.")]
    BloodHitFxVisualizer _bloodHitFx;

    [SerializeField, Tooltip("Relays hit FX to a spawned ghost visualizer, if present.")]
    BoneSnapshotReplicator _boneSnapshotReplicator;

    [Header("PuppetMaster / Death")]
    [SerializeField, Tooltip("Optional PuppetMaster to activate on death.")]
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

    [SerializeField, Tooltip("Additional damping applied once the character is dead.")]
    float _deadForceMultiplier = 0.25f;

    [SerializeField, Tooltip("Master weights (mapping/pin/muscle) applied to the PuppetMaster once dead.")]
    float _deadMasterWeight = 0.3f;

    // Runtime
    Coroutine _hitImpulseRoutine;

    public Transform OwnerRoot => _ownerRoot ? _ownerRoot : transform.root;
    public IReadOnlyList<HitBox> HitBoxes => _hitBoxes;
    public Animator Animator => _animator;

    void Awake()
    {
        if (!_state) _state = GetComponent<CharacterState>();
        if (!_ownerRoot) _ownerRoot = transform.root;
        if (!_puppetMaster) _puppetMaster = GetComponentInChildren<PuppetMaster>(true);
        if (!_animator) _animator = GetComponentInChildren<Animator>(true);
        if (_ikSolvers == null || _ikSolvers.Length == 0) _ikSolvers = GetComponentsInChildren<IK>(true);
        if (!_bloodHitFx) _bloodHitFx = GetComponentInChildren<BloodHitFxVisualizer>(true);
        if (!_boneSnapshotReplicator) _boneSnapshotReplicator = GetComponent<BoneSnapshotReplicator>();

        RefreshHitBoxes();
    }

    public bool CanBeShot(NetworkObject shooter, Vector3 point, Vector3 normal)
    {
        return _state != null;
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
        if (_state == null || !_state.IsServer)
            return;

        var finalDamage = Mathf.RoundToInt(Mathf.Max(0f, damage));
        if (finalDamage <= 0)
            return;

        // Propagate local hit FX to all observers (ghosts + owner).
        RPC_PlayHitFx(hitPoint, -hitDir, hitBoxIndex);

        bool wasAlive = _state.State == LifeState.Alive;

        _state.ServerDamage(finalDamage, shooter);

        // Lethal: alive -> dead
        if (wasAlive && _state.State == LifeState.Dead)
        {
            HandleDeath(bodyPart, hitPoint, hitDir, force, puppetMasterMuscleIndex);
        }
        // Non-lethal: apply flinch impulse
        else if (wasAlive && _state.State == LifeState.Alive)
        {
            ApplyNonLethalHitImpulse(hitPoint, hitDir, force, puppetMasterMuscleIndex);
        }
        // Already-dead ragdoll: apply impulse and FX only
        else if (!wasAlive && _state.State == LifeState.Dead)
        {
            ApplyPuppetMasterImpulse(
                hitPoint,
                hitDir,
                force,
                puppetMasterMuscleIndex,
                applyGlobalForceMultiplier: true,
                applyDeadForceReduction: true);
        }
    }

    void HandleDeath(
        BodyPart bodyPart,
        Vector3 hitPoint,
        Vector3 hitDir,
        float force,
        int puppetMasterMuscleIndex)
    {
        DeathProfile profile;

        switch (bodyPart)
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

        ActivateDeathRagdoll(hitPoint, hitDir, force, puppetMasterMuscleIndex, profile);
    }

    void ActivateDeathRagdoll(
        Vector3 hitPoint,
        Vector3 hitDir,
        float baseForce,
        int puppetMasterMuscleIndex,
        DeathProfile profile)
    {
        DisableAnimationSystems();

        if (_puppetMaster == null)
            return;

        _puppetMaster.gameObject.SetActive(true);
        _puppetMaster.enabled = true;
        _puppetMaster.mode = PuppetMaster.Mode.Active;
        _puppetMaster.state = PuppetMaster.State.Alive;

        // Clone base settings and override per-profile fields
        var killSettings = _deathStateSettings;
        killSettings.killDuration = profile.killDuration;
        killSettings.deadMuscleWeight = profile.deadMuscleWeight;
        killSettings.deadMuscleDamper = profile.deadMuscleDamper;

        _puppetMaster.Kill(killSettings);

        ApplyDeadMasterWeights();

        // Apply impulse scaled by global and per-profile multipliers
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

        if (applyDeadForceReduction && _state != null && _state.State == LifeState.Dead)
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

        _puppetMaster.mappingWeight = _deadMasterWeight;
        _puppetMaster.pinWeight = _deadMasterWeight;
        _puppetMaster.muscleWeight = _deadMasterWeight;
    }

    // -------- FX --------

    [ObserversRpc]
    void RPC_PlayHitFx(Vector3 hitPoint, Vector3 surfaceNormal, int hitBoxIndex)
    {
        // Parent FX to the specific hit box (bone) when possible so decals stick to the
        // moving limb instead of world-space root. Falls back to the owner root when
        // the hit box cannot be resolved (eg. index out of range).
        Transform spawnParent = GetHitBoxTransform(hitBoxIndex);
        if (spawnParent == null)
            spawnParent = OwnerRoot;

        _bloodHitFx?.PlayHitFx(hitPoint, surfaceNormal, spawnParent);
        _boneSnapshotReplicator?.RelayHitFxToGhost(hitPoint, surfaceNormal, spawnParent);
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
        if (_state != null && _state.State != LifeState.Alive)
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
        var muscles = _puppetMaster.muscles;
        if (muscles == null || muscles.Length == 0)
            yield break;

        // Cache current mode (usually Kinematic while alive)
        var previousMode = _puppetMaster.mode;

        // Briefly go Active so the impulse actually moves the ragdoll
        _puppetMaster.mode = PuppetMaster.Mode.Active;

        if (_hitImpulseDelay > 0f)
            yield return new WaitForSeconds(_hitImpulseDelay);

        // If they died during the delay, let death logic take over
        if (_state != null && _state.State == LifeState.Dead)
            yield break;

        // Scale and clamp the flinch force so it can't knock them over
        float clamped = Mathf.Min(force, _hitImpulseMaxForce);
        float flinchForceMag = clamped * _hitImpulseForceMultiplier;
        if (flinchForceMag <= 0f)
            yield break;

        Vector3 impactForce = hitDir.normalized * flinchForceMag;

        int targetIndex = (puppetMasterMuscleIndex >= 0 && puppetMasterMuscleIndex < muscles.Length)
            ? puppetMasterMuscleIndex
            : 0;

        muscles[targetIndex].rigidbody.AddForceAtPosition(impactForce, hitPoint, ForceMode.Impulse);

        // Keep Active for a very short time so the flinch is visible but can't fully topple them
        yield return new WaitForSeconds(_hitImpulseDuration);

        // Don't override full ragdoll if they've died since
        if (_state != null && _state.State == LifeState.Dead)
            yield break;

        _puppetMaster.mode = previousMode;
        _hitImpulseRoutine = null;
    }
}
