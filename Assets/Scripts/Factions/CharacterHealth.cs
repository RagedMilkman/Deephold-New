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
    [SerializeField, Tooltip("One-shot blood burst spawned on hit.")] GameObject _bloodImpactPrefab;
    [SerializeField, Tooltip("Decal projector spawned at the hit location.")] GameObject _bloodDecalPrefab;

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

    // Runtime
    Coroutine _hitImpulseRoutine;

    public Transform OwnerRoot => _ownerRoot ? _ownerRoot : transform.root;
    public IReadOnlyList<HitBox> HitBoxes => _hitBoxes;

    void Awake()
    {
        if (!_state) _state = GetComponent<CharacterState>();
        if (!_ownerRoot) _ownerRoot = transform.root;
        if (!_puppetMaster) _puppetMaster = GetComponentInChildren<PuppetMaster>(true);
        if (!_animator) _animator = GetComponentInChildren<Animator>(true);
        if (_ikSolvers == null || _ikSolvers.Length == 0) _ikSolvers = GetComponentsInChildren<IK>(true);

        RefreshHitBoxes();
    }

    public bool CanBeShot(NetworkObject shooter, Vector3 point, Vector3 normal)
    {
        return _state != null && _state.State == LifeState.Alive;
    }

    public void RefreshHitBoxes()
    {
        _hitBoxes.Clear();
        foreach (var hitBox in GetComponentsInChildren<HitBox>(true))
        {
            hitBox.SetOwner(this);
            _hitBoxes.Add(hitBox);
        }
    }

    public void OnHit(
        BodyPart bodyPart,
        float damage,
        Vector3 hitPoint,
        Vector3 hitDir,
        float force,
        int puppetMasterMuscleIndex,
        NetworkObject shooter = null)
    {
        if (_state == null || !_state.IsServer)
            return;

        var finalDamage = Mathf.RoundToInt(Mathf.Max(0f, damage));
        if (finalDamage <= 0)
            return;

        // Propagate local hit FX to all observers (ghosts + owner).
        RPC_PlayHitFx(hitPoint, -hitDir);

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

        // Apply impulse scaled by global and per-profile multipliers
        float totalForce = baseForce * _puppetMasterForceMultiplier * profile.forceMultiplier;
        ApplyPuppetMasterImpulse(hitPoint, hitDir, totalForce, puppetMasterMuscleIndex);
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
        int puppetMasterMuscleIndex)
    {
        if (_puppetMaster == null)
            return;

        var muscles = _puppetMaster.muscles;
        if (muscles == null || muscles.Length == 0)
            return;

        var impactForce = hitDir.normalized * force;
        var targetIndex = (puppetMasterMuscleIndex >= 0 && puppetMasterMuscleIndex < muscles.Length)
            ? puppetMasterMuscleIndex
            : 0;

        muscles[targetIndex].rigidbody.AddForceAtPosition(impactForce, hitPoint, ForceMode.Impulse);
    }

    // -------- FX --------

    [ObserversRpc]
    void RPC_PlayHitFx(Vector3 hitPoint, Vector3 surfaceNormal)
    {
        SpawnBloodFx(hitPoint, surfaceNormal);
    }

    void SpawnBloodFx(Vector3 hitPoint, Vector3 surfaceNormal)
    {
        if (_bloodImpactPrefab == null && _bloodDecalPrefab == null)
            return;

        Quaternion rotation = surfaceNormal.sqrMagnitude > 0f
            ? Quaternion.LookRotation(surfaceNormal)
            : Quaternion.identity;

        if (_bloodImpactPrefab != null)
            Instantiate(_bloodImpactPrefab, hitPoint, rotation);

        if (_bloodDecalPrefab != null)
            Instantiate(_bloodDecalPrefab, hitPoint, rotation);
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
