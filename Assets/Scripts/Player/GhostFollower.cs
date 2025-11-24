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

        SyncTransforms(_target, transform);
    }

    private void SyncTransforms(Transform source, Transform destination)
    {
        destination.SetPositionAndRotation(source.position, source.rotation);
        destination.localScale = source.localScale;

        foreach (Transform sourceChild in source)
        {
            Transform destinationChild = destination.Find(sourceChild.name);
            if (destinationChild != null)
                SyncTransforms(sourceChild, destinationChild);
        }
    }
}
