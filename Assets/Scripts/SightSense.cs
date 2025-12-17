using System.Collections.Generic;
using UnityEngine;

public class SightSense : MonoBehaviour, ISense
{
    [SerializeField, Min(0f)] private float visionRange = 20f;
    [SerializeField, Range(0f, 180f)] private float fieldOfView = 90f;
    [SerializeField] private LayerMask observableLayers = Physics.DefaultRaycastLayers;
    [SerializeField] private LayerMask obstacleLayers = Physics.DefaultRaycastLayers;
    [SerializeField] private bool debugDraw = false;
    [SerializeField] private Color debugColor = Color.cyan;
    [SerializeField] private Color debugHitColor = Color.green;
    [SerializeField] private Color debugBlockedColor = Color.red;

    private readonly List<Observation> buffer = new();
    private readonly List<Observation> lastObservations = new();

    public List<Observation> GetObservations()
    {
        buffer.Clear();
        lastObservations.Clear();

        var origin = transform.position;
        var hits = Physics.OverlapSphere(origin, visionRange, observableLayers, QueryTriggerInteraction.Ignore);

        foreach (var hit in hits)
        {
            if (hit.transform == transform)
                continue;

            var target = hit.transform;
            var direction = target.position - origin;
            var distance = direction.magnitude;

            if (distance <= 0.0001f)
                continue;

            var angle = Vector3.Angle(transform.forward, direction);
            if (angle > fieldOfView * 0.5f)
                continue;

            if (!HasLineOfSight(origin, direction, distance, target))
            {
                DrawDebugRay(origin, target.position, debugBlockedColor);
                continue;
            }

            var id = target ? target.GetInstanceID().ToString() : string.Empty;
            var observation = Observation.ForCharacter(target, hit.gameObject, id, null, null, BeliefSource.Sight, 1f, Time.time);
            buffer.Add(observation);
            lastObservations.Add(observation);

            DrawDebugRay(origin, target.position, debugHitColor);
        }

        return new List<Observation>(buffer);
    }

    private bool HasLineOfSight(Vector3 origin, Vector3 direction, float distance, Transform target)
    {
        if (!Physics.Raycast(origin, direction.normalized, out RaycastHit hit, distance, obstacleLayers, QueryTriggerInteraction.Ignore))
            return true;

        return hit.transform == target || hit.transform.IsChildOf(target);
    }

    private void OnDrawGizmosSelected()
    {
        if (!debugDraw)
            return;

        Gizmos.color = debugColor;
        Gizmos.DrawWireSphere(transform.position, visionRange);

        var leftDir = Quaternion.AngleAxis(-fieldOfView * 0.5f, Vector3.up) * transform.forward;
        var rightDir = Quaternion.AngleAxis(fieldOfView * 0.5f, Vector3.up) * transform.forward;

        Gizmos.DrawLine(transform.position, transform.position + leftDir.normalized * visionRange);
        Gizmos.DrawLine(transform.position, transform.position + rightDir.normalized * visionRange);

        foreach (var obs in lastObservations)
        {
            if (obs?.Location)
                Gizmos.DrawLine(transform.position, obs.Location.position);
        }
    }

    private void DrawDebugRay(Vector3 start, Vector3 end, Color color)
    {
        if (!debugDraw)
            return;

        Debug.DrawLine(start, end, color, 0.1f);
    }
}
