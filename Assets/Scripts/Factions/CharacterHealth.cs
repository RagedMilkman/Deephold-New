using System.Collections.Generic;
using FishNet.Object;
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
    [SerializeField, Tooltip("Optional PuppetMaster to briefly enable when hit.")] PuppetMaster _puppetMaster;
    [SerializeField, Tooltip("How long to keep PuppetMaster active after an impact.")] float _puppetMasterHitDuration = 0.75f;
    [SerializeField, Tooltip("Delay before applying force so PuppetMaster can fully activate.")] float _puppetMasterForceDelay = 0.05f;
    [SerializeField, Tooltip("Multiplier for forces applied to PuppetMaster muscles on hit.")] float _puppetMasterForceMultiplier = 1f;

    Coroutine _puppetMasterResetRoutine;
    PuppetMaster.Mode _cachedPuppetMode;
    PuppetMaster.State _cachedPuppetState;
    bool _cachedPuppetEnabled;
    bool _cachedPuppetActiveSelf;

    public Transform OwnerRoot => _ownerRoot ? _ownerRoot : transform.root;
    public IReadOnlyList<HitBox> HitBoxes => _hitBoxes;

    void Awake()
    {
        if (!_state) _state = GetComponent<CharacterState>();
        if (!_ownerRoot) _ownerRoot = transform.root;
        if (!_puppetMaster) _puppetMaster = GetComponentInChildren<PuppetMaster>(true);

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

    public void OnHit(BodyPart bodyPart, float damage, Vector3 hitPoint, Vector3 hitDir, float force, int puppetMasterMuscleIndex, NetworkObject shooter = null)
    {
        if (_state == null || !_state.IsServer)
            return;

        var finalDamage = Mathf.RoundToInt(Mathf.Max(0f, damage));
        if (finalDamage <= 0)
            return;

        ApplyPuppetMasterImpact(hitPoint, hitDir, force, puppetMasterMuscleIndex);
        _state.ServerDamage(finalDamage, shooter, bodyPart);
    }

    void ApplyPuppetMasterImpact(Vector3 hitPoint, Vector3 hitDir, float force, int puppetMasterMuscleIndex)
    {
        if (_state != null && _state.State == LifeState.Dead)
            return;

        if (_puppetMaster == null)
            return;

        var muscles = _puppetMaster.muscles;
        if (muscles == null || muscles.Length == 0)
            return;

        CachePuppetMasterState();

        ActivatePuppetMasterForImpact();

        if (_puppetMasterResetRoutine != null)
            StopCoroutine(_puppetMasterResetRoutine);

        _puppetMasterResetRoutine = StartCoroutine(ApplyPuppetForceAndReset(hitPoint, hitDir, force, puppetMasterMuscleIndex));
    }

    void ActivatePuppetMasterForImpact()
    {
        _puppetMaster.gameObject.SetActive(true);
        _puppetMaster.enabled = true;
        _puppetMaster.mode = PuppetMaster.Mode.Active;
        _puppetMaster.state = PuppetMaster.State.Alive;

        var muscles = _puppetMaster.muscles;
        for (int i = 0; i < muscles.Length; i++)
        {
            muscles[i].rigidbody.WakeUp();
        }

        Physics.SyncTransforms();
    }

    void CachePuppetMasterState()
    {
        _cachedPuppetMode = _puppetMaster.mode;
        _cachedPuppetState = _puppetMaster.state;
        _cachedPuppetEnabled = _puppetMaster.enabled;
        _cachedPuppetActiveSelf = _puppetMaster.gameObject.activeSelf;
    }

    System.Collections.IEnumerator ApplyPuppetForceAndReset(Vector3 hitPoint, Vector3 hitDir, float force, int puppetMasterMuscleIndex)
    {
        if (_puppetMasterForceDelay > 0f)
            yield return new WaitForSeconds(_puppetMasterForceDelay);

        if (_puppetMaster == null)
        {
            yield break;
        }

        var muscles = _puppetMaster.muscles;
        if (muscles == null || muscles.Length == 0)
        {
            yield break;
        }

        if (_state != null && _state.State == LifeState.Dead)
        {
            yield break;
        }

        var impactForce = hitDir.normalized * force * _puppetMasterForceMultiplier;
        if (puppetMasterMuscleIndex >= 0 && puppetMasterMuscleIndex < muscles.Length)
            muscles[puppetMasterMuscleIndex].rigidbody.AddForceAtPosition(impactForce, hitPoint, ForceMode.Impulse);
        else
            muscles[0].rigidbody.AddForceAtPosition(impactForce, hitPoint, ForceMode.Impulse);

        yield return new WaitForSeconds(_puppetMasterHitDuration);

        if (_puppetMaster == null)
            yield break;

        if (_state != null && _state.State == LifeState.Dead)
            yield break;

        _puppetMaster.mode = _cachedPuppetMode;
        _puppetMaster.state = _cachedPuppetState;
        _puppetMaster.enabled = _cachedPuppetEnabled;
        _puppetMaster.gameObject.SetActive(_cachedPuppetActiveSelf);

        _puppetMasterResetRoutine = null;
    }
}
