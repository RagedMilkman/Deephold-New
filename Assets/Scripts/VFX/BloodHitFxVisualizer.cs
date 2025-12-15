using UnityEngine;

/// <summary>
/// Spawns blood impact and decal FX at a hit location.
/// </summary>
public class BloodHitFxVisualizer : MonoBehaviour
{
    [SerializeField, Tooltip("One-shot blood burst spawned on hit.")]
    private GameObject _bloodImpactPrefab;

    [SerializeField, Tooltip("Optional blood burst spawned when a projectile exits.")]
    private GameObject _bloodExitPrefab;

    [SerializeField, Tooltip("Decal projector spawned at the hit location.")]
    private GameObject _bloodDecalPrefab;

    [SerializeField, Tooltip("Uniform scale applied to the entry impact effect.")]
    private float _entryImpactScale = 0.5f;

    [SerializeField, Tooltip("Minimum force required before spawning an exit wound.")]
    private float _exitMinimumForce = 5f;

    [SerializeField, Tooltip("Multiplier applied to remaining force to scale the exit wound.")]
    private float _exitScalePerForce = 0.02f;

    public void PlayHitFx(
        Vector3 hitPoint,
        Vector3 surfaceNormal,
        Transform spawnParent = null,
        float impactForce = 0f,
        Vector3 hitDirection = default)
    {
        if (_bloodImpactPrefab == null && _bloodDecalPrefab == null)
            return;

        Quaternion rotation = surfaceNormal.sqrMagnitude > 0f
            ? Quaternion.LookRotation(surfaceNormal)
            : Quaternion.identity;

        if (_bloodImpactPrefab != null)
        {
            var entryImpact = Instantiate(_bloodImpactPrefab, hitPoint, rotation, spawnParent);
            entryImpact.transform.localScale *= _entryImpactScale;
        }

        if (_bloodDecalPrefab != null)
            Instantiate(_bloodDecalPrefab, hitPoint, rotation, spawnParent);

        float remainingForce = impactForce - _exitMinimumForce;
        if (remainingForce <= 0f)
            return;

        GameObject exitPrefab = _bloodExitPrefab != null ? _bloodExitPrefab : _bloodImpactPrefab;
        if (exitPrefab == null)
            return;

        Vector3 exitNormal = hitDirection != Vector3.zero ? hitDirection.normalized : -surfaceNormal;
        Quaternion exitRotation = exitNormal.sqrMagnitude > 0f
            ? Quaternion.LookRotation(exitNormal)
            : rotation;

        var exitImpact = Instantiate(exitPrefab, hitPoint, exitRotation, spawnParent);
        float exitScale = remainingForce * _exitScalePerForce;
        exitImpact.transform.localScale *= exitScale;
    }
}
