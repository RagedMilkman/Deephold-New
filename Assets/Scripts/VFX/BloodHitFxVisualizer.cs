using UnityEngine;

/// <summary>
/// Spawns blood impact and decal FX at a hit location.
/// </summary>
public class BloodHitFxVisualizer : MonoBehaviour
{
    [SerializeField, Tooltip("One-shot blood burst spawned on hit.")]
    private GameObject _bloodImpactPrefab;

    [SerializeField, Tooltip("Decal projector spawned at the hit location.")]
    private GameObject _bloodDecalPrefab;

    public void PlayHitFx(Vector3 hitPoint, Vector3 surfaceNormal, Transform spawnParent = null)
    {
        if (_bloodImpactPrefab == null && _bloodDecalPrefab == null)
            return;

        Quaternion rotation = surfaceNormal.sqrMagnitude > 0f
            ? Quaternion.LookRotation(surfaceNormal)
            : Quaternion.identity;

        if (_bloodImpactPrefab != null)
            Instantiate(_bloodImpactPrefab, hitPoint, rotation, spawnParent);

        if (_bloodDecalPrefab != null)
            Instantiate(_bloodDecalPrefab, hitPoint, rotation, spawnParent);
    }
}
