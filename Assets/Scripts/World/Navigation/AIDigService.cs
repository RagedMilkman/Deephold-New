using UnityEngine;

public class AIDigService : MonoBehaviour, IAIDigService
{
    [SerializeField] LayerMask digMask;
    [SerializeField] float probeRadius = 0.35f;
    [SerializeField] float probeHeightOffset = 0.5f;
    [SerializeField] float maxProbeDistance = 1.5f;
    [SerializeField] int damagePerHit = 1;
    [SerializeField] float digCooldown = 0.5f;

    float nextDigTime;
     
    public bool TryDig(Vector3 origin, Vector3 targetPosition)
    {
        if (Time.time < nextDigTime)
            return false;

        Vector3 direction = targetPosition - origin;
        float distance = direction.magnitude;
        if (distance <= 0f)
            return false;

        direction /= distance;

        float castDistance = Mathf.Min(distance, maxProbeDistance);
        Vector3 castOrigin = origin + Vector3.up * probeHeightOffset;
        int mask = digMask.value == 0 ? ~0 : digMask.value;

        if (!Physics.SphereCast(castOrigin, probeRadius, direction, out var hit, castDistance, mask, QueryTriggerInteraction.Ignore))
            return false;

        if (!hit.collider)
            return false;

        var block = hit.collider.GetComponentInParent<MineableBlock>();
        if (!block || block.IsInvincible)
            return false;

        block.ReportHit(Mathf.Max(1, damagePerHit));
        nextDigTime = Time.time + Mathf.Max(0f, digCooldown);
        return true;
    }
}
