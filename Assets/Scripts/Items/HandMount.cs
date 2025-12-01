using UnityEngine;

/// <summary>
/// Identifies a transform as the mount location for a specific hand on an item.
/// </summary>
public class HandMount : MonoBehaviour
{
    /// <summary>
    /// The hand that should use this mount point.
    /// </summary>
    public enum HandSide
    {
        Left,
        Right,
    }

    public enum HandPart
    {
        Palm,
        Wrist,
    }

    [SerializeField]
    [Tooltip("The hand that should be aligned to this mount point.")]
    private HandSide hand = HandSide.Right;

    [SerializeField]
    [Tooltip("The hand part to match this mount point.")]
    private HandPart part = HandPart.Palm;

    [SerializeField]
    [Tooltip("Optional override transform for the mount point. Defaults to this object's transform.")]
    private Transform mountTransform;

    /// <summary>
    /// The hand assigned to this mount point.
    /// </summary>
    public HandSide Hand => hand;

    /// <summary>
    /// The hand assigned to this mount point.
    /// </summary>
    public HandPart Part => part;

    /// <summary>
    /// The transform that should be used as the mounting position.
    /// </summary>
    public Transform MountTransform => mountTransform != null ? mountTransform : transform;

    private void Reset()
    {
        mountTransform = transform;
    }
}
