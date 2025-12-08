using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Handles health and damage routing for a character with multiple hitboxes.
/// </summary>
public class CharacterHealth : NetworkBehaviour
{
    [SerializeField] CharacterState _state;
    [SerializeField] Transform _ownerRoot;
    [SerializeField] List<HitBox> _hitBoxes = new();

    public Transform OwnerRoot => _ownerRoot ? _ownerRoot : transform.root;
    public IReadOnlyList<HitBox> HitBoxes => _hitBoxes;

    void Awake()
    {
        if (!_state) _state = GetComponent<CharacterState>();
        if (!_ownerRoot) _ownerRoot = transform.root;

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

        _state.ServerDamage(finalDamage, shooter);
    }
}
