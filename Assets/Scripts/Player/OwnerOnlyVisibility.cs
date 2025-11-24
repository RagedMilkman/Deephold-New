using FishNet.Object;
using UnityEngine;

/// <summary>
/// Restricts the player representation to the owning client only.
/// </summary>
public sealed class OwnerOnlyVisibility : NetworkBehaviour
{
    [SerializeField] private GameObject _target;

    private Renderer[] _renderers;
    private Canvas[] _canvases;

    private void Awake()
    {
        if (_target == null)
            _target = gameObject;

        _renderers = _target.GetComponentsInChildren<Renderer>(true);
        _canvases = _target.GetComponentsInChildren<Canvas>(true);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        SetActiveState();
    }

    private void SetActiveState()
    {
        if (_target == null)
            return;

        SetVisibility(IsOwner);
    }

    private void SetVisibility(bool visible)
    {
        if (_renderers != null)
        {
            foreach (var renderer in _renderers)
                renderer.enabled = visible;
        }

        if (_canvases != null)
        {
            foreach (var canvas in _canvases)
                canvas.enabled = visible;
        }
    }
}
