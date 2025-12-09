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

    [Header("PuppetMaster / Death")]
    [SerializeField, Tooltip("Optional PuppetMaster to activate on death.")]
    PuppetMaster _puppetMaster;

    [SerializeField, Tooltip("State settings to use when killing the PuppetMaster on death.")]
    PuppetMaster.StateSettings _deathStateSettings;

    [SerializeField, Tooltip("Animator controlling the character's death poses.")]
    Animator _animator;

    [SerializeField, Tooltip("IK solvers to disable when transitioning to ragdoll.")]
    IK[] _ikSolvers;

    [SerializeField, Tooltip("Multiplier for forces applied to PuppetMaster muscles on hit.")]
    float _puppetMasterForceMultiplier = 1f;

    [Header("Non-lethal hit flinch")]
    [SerializeField, Tooltip("How long PuppetMaster stays Active for a non-lethal hit flinch.")]
    float _hitImpulseDuration = 0.2f;

    [SerializeField, Tooltip("Delay before applying non-lethal hit force (allows PM to wake up).")]
    float _hitImpulseDelay = 0.02f;

    // Pending data for torso death -> ragdoll handoff
    Vector3 _pendingHitPoint;
    Vector3 _pendingHitDir;
    float _pendingForce;
    int _pendingMuscleIndex;
    bool _waitingForTorsoRagdoll;

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
        _waitingForTorsoRagdoll = false;

        switch (bodyPart)
        {
            case BodyPart.Head:
                // Instant ragdoll for headshot
                ActivateImmediateRagdoll(hitPoint, hitDir, force, puppetMasterMuscleIndex, 0.1f);
                break;

            case BodyPart.Torso:
                // Play dying animation, then ragdoll on anim event
                BeginTorsoDeath(hitPoint, hitDir, force, puppetMasterMuscleIndex);
                break;

            default:
                // Fallback: instant ragdoll
                ActivateImmediateRagdoll(hitPoint, hitDir, force, puppetMasterMuscleIndex, 0.1f);
                break;
        }
    }

    void BeginTorsoDeath(
        Vector3 hitPoint,
        Vector3 hitDir,
        float force,
        int puppetMasterMuscleIndex)
    {
        _pendingHitPoint = hitPoint;
        _pendingHitDir = hitDir;
        _pendingForce = force;
        _pendingMuscleIndex = puppetMasterMuscleIndex;
        _waitingForTorsoRagdoll = true;

        // Ensure PM isn't interfering while anim plays
        if (_puppetMaster != null)
        {
            _puppetMaster.gameObject.SetActive(false);
            _puppetMaster.enabled = false;
        }

        if (_animator)
            _animator.SetTrigger("Die_Torso");
    }

    /// <summary>
    /// Called by animation event when the torso death anim hits the ground.
    /// </summary>
    public void OnTorsoDeathHitGround()
    {
        if (!_waitingForTorsoRagdoll)
            return;

        _waitingForTorsoRagdoll = false;
        ActivateImmediateRagdoll(_pendingHitPoint, _pendingHitDir, _pendingForce, _pendingMuscleIndex, 0.3f);
    }

    void ActivateImmediateRagdoll(
        Vector3 hitPoint,
        Vector3 hitDir,
        float force,
        int puppetMasterMuscleIndex,
        float killDuration)
    {
        DisableAnimationSystems();

        if (_puppetMaster == null)
            return;

        _puppetMaster.gameObject.SetActive(true);
        _puppetMaster.enabled = true;
        _puppetMaster.mode = PuppetMaster.Mode.Active;
        _puppetMaster.state = PuppetMaster.State.Alive;

        var killSettings = _deathStateSettings;
        killSettings.killDuration = killDuration;
        _puppetMaster.Kill(killSettings);

        ApplyPuppetMasterImpulse(hitPoint, hitDir, force, puppetMasterMuscleIndex);
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

        var impactForce = hitDir.normalized * force * _puppetMasterForceMultiplier;
        var targetIndex = (puppetMasterMuscleIndex >= 0 && puppetMasterMuscleIndex < muscles.Length)
            ? puppetMasterMuscleIndex
            : 0;

        muscles[targetIndex].rigidbody.AddForceAtPosition(impactForce, hitPoint, ForceMode.Impulse);
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

        var impactForce = hitDir.normalized * (force * _puppetMasterForceMultiplier);
        int targetIndex = (puppetMasterMuscleIndex >= 0 && puppetMasterMuscleIndex < muscles.Length)
            ? puppetMasterMuscleIndex
            : 0;

        muscles[targetIndex].rigidbody.AddForceAtPosition(impactForce, hitPoint, ForceMode.Impulse);

        // Keep Active for a short time so the flinch is visible
        yield return new WaitForSeconds(_hitImpulseDuration);

        // Don't override full ragdoll if they've died since
        if (_state != null && _state.State == LifeState.Dead)
            yield break;

        _puppetMaster.mode = previousMode;
        _hitImpulseRoutine = null;
    }
}
