using FishNet.Object;
using UnityEngine;

/// <summary>
/// Restricts the player representation to the owning client only.
/// </summary>
public sealed class OwnerOnlyVisibility : NetworkBehaviour
{
    [SerializeField] private GameObject _target;

    private void Awake()
    {
        if (_target == null)
            _target = gameObject;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        SetActiveState();
    }

    private void SetActiveState()
    {
        if (_target != null)
            _target.SetActive(IsOwner);
    }
}
