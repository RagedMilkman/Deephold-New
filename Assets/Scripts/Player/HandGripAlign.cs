using UnityEngine;

/// <summary>
/// After the 2-bone IK has positioned the wristJoint,
/// rotate the wrist so that the character's handGrip
/// matches the item's itemHandMount orientation.
/// 
/// Character:
///   wristJoint  = wrist bone
///   handGrip    = child of wrist, in the palm
/// Item:
///   itemHandMount = child on the item where the hand should sit
/// </summary>
[ExecuteAlways]
// Run after the IK solver (350) so we can adjust the wrist orientation without it
// being overwritten in the same frame.
[DefaultExecutionOrder(400)]
public class HandGripAlign : MonoBehaviour
{
    [Header("Character Rig")]
    public Transform wristJoint;   // wrist bone on the character
    public Transform handGrip;     // child of wristJoint (palm marker)

    [Header("Item Mount")]
    public Transform itemHandMount; // child transform on the item

    [Header("Settings")]
    [Range(0f, 1f)]
    public float rotationWeight = 1f;   // how strongly to align the hand

    public bool solveInEditMode = true;

    private LimbIKBoneConstraint wristConstraint;
    private Transform cachedWristJoint;
    private Transform cachedItemMount;
    private HandMountPoseAuthoring cachedPoseAuthoring;
    private Quaternion defaultWristLocalRotation;
    private bool hasDefaultWristRotation;

    private void OnValidate()
    {
        RefreshWristBinding();
        EnsureDefaultRotation();
        RefreshPoseAuthoring();
    }

    private void Awake()
    {
        RefreshWristBinding();
        EnsureDefaultRotation();
        RefreshPoseAuthoring();
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying && !solveInEditMode)
            return;

        AlignImmediate();
    }

    /// <summary>
    /// Immediately aligns the wrist to the current target. Can be invoked manually when
    /// targets are reassigned to guarantee the pose is refreshed in the same frame.
    /// </summary>
    public void AlignImmediate()
    {
        if (wristJoint == null)
            return;

        RefreshWristBinding();
        EnsureDefaultRotation();

        RefreshPoseAuthoring();

        if (!handGrip || !itemHandMount)
        {
            if (!hasDefaultWristRotation)
                return;

            Quaternion parentWorld = wristJoint.parent ? wristJoint.parent.rotation : Quaternion.identity;
            Quaternion currentWristWorld = wristJoint.rotation;
            Quaternion desiredWorld = parentWorld * defaultWristLocalRotation;
            Quaternion blended = Quaternion.Slerp(currentWristWorld, desiredWorld, rotationWeight);

            if (wristConstraint != null)
            {
                Quaternion currentLocal = wristJoint.localRotation;
                blended = wristConstraint.ConstrainRotation(parentWorld, blended, currentLocal);
            }

            wristJoint.rotation = blended;
            return;
        }

        if (cachedPoseAuthoring != null)
            cachedPoseAuthoring.ApplyOrientation();

        // Current world rotations
        Quaternion wristRot = wristJoint.rotation;
        Quaternion handGripRot = handGrip.rotation;      // source orientation
        Quaternion targetRot = itemHandMount.rotation; // desired orientation for handGrip

        // Rotate wrist so that handGrip's rotation becomes itemHandMount's rotation
        Quaternion delta = targetRot * Quaternion.Inverse(handGripRot);
        Quaternion desiredWristRot = delta * wristRot;

        // Blend
        desiredWristRot = Quaternion.Slerp(wristRot, desiredWristRot, rotationWeight);

        // Optional: clamp with your constraint on the wrist
        if (wristConstraint != null)
        {
            Quaternion parentWorld = wristJoint.parent ? wristJoint.parent.rotation : Quaternion.identity;
            Quaternion currentLocal = wristJoint.localRotation;
            desiredWristRot = wristConstraint.ConstrainRotation(parentWorld, desiredWristRot, currentLocal);
        }

        wristJoint.rotation = desiredWristRot;
    }

    private void RefreshWristBinding()
    {
        if (wristJoint == cachedWristJoint)
            return;

        cachedWristJoint = wristJoint;
        wristConstraint = cachedWristJoint ? cachedWristJoint.GetComponent<LimbIKBoneConstraint>() : null;
        hasDefaultWristRotation = false;
    }

    private void EnsureDefaultRotation()
    {
        if (hasDefaultWristRotation)
            return;

        if (!wristJoint)
            return;

        defaultWristLocalRotation = wristJoint.localRotation;
        hasDefaultWristRotation = true;
    }

    private void RefreshPoseAuthoring()
    {
        if (itemHandMount != cachedItemMount)
        {
            cachedItemMount = itemHandMount;
            cachedPoseAuthoring = null;
        }

        if (!cachedItemMount)
        {
            cachedPoseAuthoring = null;
            return;
        }

        if (!cachedPoseAuthoring)
            cachedPoseAuthoring = cachedItemMount.GetComponent<HandMountPoseAuthoring>();
    }
}
