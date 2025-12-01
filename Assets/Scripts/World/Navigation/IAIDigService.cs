using UnityEngine;

public interface IAIDigService
{
    /// <summary>
    /// Attempt to dig any blocking mineable object between origin and the target position.
    /// Returns true if a dig attempt occurred (i.e., a block was hit).
    /// </summary>
    bool TryDig(Vector3 origin, Vector3 targetPosition);
}
