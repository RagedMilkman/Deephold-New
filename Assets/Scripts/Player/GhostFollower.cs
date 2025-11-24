using UnityEngine;

/// <summary>
/// Local-only visual follower for remote player ghosts.
/// </summary>
public class GhostFollower : MonoBehaviour
{
    [SerializeField] private Transform _target;

    public Transform Target
    {
        get => _target;
        set => _target = value;
    }

    public void SetTarget(Transform target)
    {
        _target = target;
    }

    private void LateUpdate()
    {
        if (_target == null)
            return;

        transform.position = _target.position;
        transform.rotation = _target.rotation;
    }
}
