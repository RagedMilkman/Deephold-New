using System.Collections.Generic;
using FishNet.Connection;
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

    public override bool OnCheckObserver(NetworkConnection connection)
    {
        return connection == Owner;
    }

    public override void OnRebuildObservers(HashSet<NetworkConnection> newObservers, bool initialize)
    {
        if (Owner != null)
            newObservers.Add(Owner);
    }

    private void SetActiveState()
    {
        if (_target != null)
            _target.SetActive(IsOwner);
    }
}
