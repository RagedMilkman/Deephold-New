using RootMotion.Dynamics;
using RootMotion.FinalIK;
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

    private void Awake()
    {
        DisableGhostBehaviours();
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

    private void DisableGhostBehaviours()
    {
        foreach (PuppetMaster puppetMaster in GetComponentsInChildren<PuppetMaster>(true))
            puppetMaster.enabled = false;

        foreach (IK ik in GetComponentsInChildren<IK>(true))
            ik.enabled = false;

        foreach (Animator animator in GetComponentsInChildren<Animator>(true))
            animator.enabled = false;
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
