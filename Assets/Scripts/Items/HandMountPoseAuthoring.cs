using UnityEngine;

/// <summary>
/// Helper for configuring the orientation of a hand mount using simple axis selections
/// instead of manually rotating the transform in the scene view.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class HandMountPoseAuthoring : MonoBehaviour
{
    public enum AxisDirection
    {
        PositiveX,
        NegativeX,
        PositiveY,
        NegativeY,
        PositiveZ,
        NegativeZ
    }

    [Header("Orientation")]
    [SerializeField]
    [Tooltip("Axis that should point along the palm normal (out of the hand).")]
    private AxisDirection palmAxis = AxisDirection.PositiveY;

    [SerializeField]
    [Tooltip("Axis that should point toward the fingers (forward along the grip).")]
    private AxisDirection fingerAxis = AxisDirection.PositiveZ;

    [SerializeField]
    [Tooltip("Flip the computed thumb axis for left-hand mounts to mirror the pose.")]
    private bool mirrorLeftHand = true;

    [SerializeField]
    [Tooltip("Apply the computed orientation when the component becomes enabled in play mode.")]
    private bool updateInPlayMode = true;

    private HandMount attachedMount;

    private void Awake()
    {
        CacheHandMount();
        if (Application.isPlaying)
        {
            if (updateInPlayMode)
                ApplyOrientation();
        }
        else
        {
            ApplyOrientation();
        }
    }

    private void OnEnable()
    {
        CacheHandMount();
        if (!Application.isPlaying || updateInPlayMode)
            ApplyOrientation();
    }

    private void OnValidate()
    {
        CacheHandMount();
        ApplyOrientation();
    }

    private void Reset()
    {
        CacheHandMount();
        ApplyOrientation();
    }

    /// <summary>
    /// Re-applies the configured orientation immediately.
    /// </summary>
    public void ApplyOrientation()
    {
        Vector3 palm = ResolveAxis(palmAxis);
        Vector3 finger = ResolveAxis(fingerAxis);

        if (AreParallel(palm, finger))
        {
            finger = ResolveAxis(GetPerpendicularFallback(palmAxis));
        }

        Vector3 thumb = Vector3.Cross(palm, finger);
        if (thumb.sqrMagnitude < 1e-6f)
        {
            Vector3 fallbackFinger = ResolveAxis(GetPerpendicularFallback(fingerAxis));
            thumb = Vector3.Cross(palm, fallbackFinger);
        }

        thumb = thumb.normalized;
        if (attachedMount != null && attachedMount.Hand == HandMount.HandSide.Left && mirrorLeftHand)
            thumb = -thumb;

        Vector3 forward = Vector3.Cross(thumb, palm).normalized;
        Vector3 up = palm.normalized;

        if (forward.sqrMagnitude < 1e-6f)
        {
            forward = ResolveAxis(GetPerpendicularFallback(palmAxis));
            if (attachedMount != null && attachedMount.Hand == HandMount.HandSide.Left && mirrorLeftHand)
                forward = -forward;
        }

        transform.localRotation = Quaternion.LookRotation(forward, up);
    }

    [ContextMenu("Apply Orientation")]
    private void ApplyOrientationContext()
    {
        ApplyOrientation();
    }

    private void CacheHandMount()
    {
        if (attachedMount == null)
            attachedMount = GetComponent<HandMount>() ?? GetComponentInParent<HandMount>(true);
    }

    private static bool AreParallel(Vector3 a, Vector3 b)
    {
        float magnitudeProduct = a.sqrMagnitude * b.sqrMagnitude;
        if (magnitudeProduct <= 0f)
            return true;

        float dot = Vector3.Dot(a, b);
        return Mathf.Approximately(dot * dot, magnitudeProduct);
    }

    private static Vector3 ResolveAxis(AxisDirection axis)
    {
        return axis switch
        {
            AxisDirection.PositiveX => Vector3.right,
            AxisDirection.NegativeX => Vector3.left,
            AxisDirection.PositiveY => Vector3.up,
            AxisDirection.NegativeY => Vector3.down,
            AxisDirection.PositiveZ => Vector3.forward,
            AxisDirection.NegativeZ => Vector3.back,
            _ => Vector3.forward,
        };
    }

    private static AxisDirection GetPerpendicularFallback(AxisDirection axis)
    {
        return axis switch
        {
            AxisDirection.PositiveX or AxisDirection.NegativeX => AxisDirection.PositiveY,
            AxisDirection.PositiveY or AxisDirection.NegativeY => AxisDirection.PositiveZ,
            AxisDirection.PositiveZ or AxisDirection.NegativeZ => AxisDirection.PositiveX,
            _ => AxisDirection.PositiveY,
        };
    }
}
