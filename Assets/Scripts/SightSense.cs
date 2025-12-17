using System.Collections.Generic;
using UnityEngine;

public class SightSense : MonoBehaviour, ISense
{
    [SerializeField, Min(0f)] private float visionRange = 20f;
    [SerializeField, Range(0f, 180f)] private float fieldOfView = 90f;
    [SerializeField] private LayerMask observableLayers = Physics.DefaultRaycastLayers;
    [SerializeField] private LayerMask obstacleLayers = Physics.DefaultRaycastLayers;

    private readonly List<Observation> buffer = new();

    public List<Observation> GetObservations()
    {
        buffer.Clear();

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
                continue;

            buffer.Add(new Observation(target, hit.gameObject));
        }

        return new List<Observation>(buffer);
    }

    private bool HasLineOfSight(Vector3 origin, Vector3 direction, float distance, Transform target)
    {
        if (!Physics.Raycast(origin, direction.normalized, out RaycastHit hit, distance, obstacleLayers, QueryTriggerInteraction.Ignore))
            return true;

        return hit.transform == target || hit.transform.IsChildOf(target);
    }
}
