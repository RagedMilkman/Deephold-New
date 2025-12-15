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

    [Header("Death FX")]
    [SerializeField, Tooltip("One-shot blood pool spawned at the character's feet on death.")]
    private GameObject _bloodPoolPrefab;

    [SerializeField, Tooltip("If false, death will not spawn a blood pool.")]
    private bool _spawnDeathBloodPool = true;

    [SerializeField, Tooltip("Uniform scale applied to the entry impact effect.")]
    private float _entryImpactScale = 0.5f;

    [SerializeField, Tooltip("Minimum force required before spawning an exit wound.")]
    private float _exitMinimumForce = 5f;

    [SerializeField, Tooltip("Multiplier applied to remaining force to scale the exit wound.")]
    private float _exitScalePerForce = 0.02f;

    [SerializeField, Tooltip("How far to push the exit FX along the bullet direction.")]
    private float _exitSpawnOffset = 0.02f;

    private bool _spawnedBloodPool;

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

        Vector3 exitNormal = hitDirection.sqrMagnitude > 0f ? hitDirection.normalized : -surfaceNormal;
        Quaternion exitRotation = exitNormal.sqrMagnitude > 0f
            ? Quaternion.LookRotation(exitNormal)
            : rotation;

        Vector3 exitPoint = hitPoint + (exitNormal * _exitSpawnOffset);

        var exitImpact = Instantiate(exitPrefab, exitPoint, exitRotation, spawnParent);
        float exitScale = remainingForce * _exitScalePerForce;
        exitImpact.transform.localScale *= exitScale;
    }

    public void ResetBloodPoolSpawn()
    {
        _spawnedBloodPool = false;
    }

    public void SpawnDeathBloodPool(Vector3 position, Transform parent = null)
    {
        if (!_spawnDeathBloodPool || _bloodPoolPrefab == null || _spawnedBloodPool)
            return;

        Vector3 spawnPos = position;
        Quaternion spawnRot = Quaternion.LookRotation(Vector3.up);

        Vector3 rayOrigin = position + (Vector3.up * 0.5f);
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            spawnPos = hit.point;
            spawnRot = Quaternion.LookRotation(hit.normal);
        }

        var instance = Instantiate(_bloodPoolPrefab, spawnPos, spawnRot, parent);
        if (parent != null && instance.transform.parent != parent)
            instance.transform.SetParent(parent, true);

        _spawnedBloodPool = true;
    }
}
