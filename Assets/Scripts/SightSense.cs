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
    [SerializeField] private CharacterHealth selfCharacter;

    private readonly List<Observation> buffer = new();
    private readonly List<Observation> lastObservations = new();

    private void Awake()
    {
        if (!selfCharacter)
            selfCharacter = GetComponentInParent<CharacterHealth>();
    }

    public List<Observation> GetObservations()
    {
        buffer.Clear();
        lastObservations.Clear();

        var origin = transform.position;
        var selfMotor = selfCharacter ? selfCharacter.GetComponentInChildren<TopDownMotor>(true) : null;
        var selfRoot = ResolvePositionRoot(selfCharacter, selfMotor) ?? transform.root;
        var hits = Physics.OverlapSphere(origin, visionRange, observableLayers, QueryTriggerInteraction.Ignore);
        var seen = new HashSet<CharacterHealth>();

        foreach (var hit in hits)
        {
            var characterHealth = hit.GetComponentInParent<CharacterHealth>();
            if (characterHealth == null)
                continue;

            if (seen.Contains(characterHealth))
                continue;

            var obstacleTarget = hit.transform;
            var motor = characterHealth.GetComponentInChildren<TopDownMotor>(true);
            var targetRoot = ResolvePositionRoot(characterHealth, motor);
            if (selfRoot && targetRoot == selfRoot)
                continue;
            if (obstacleTarget == transform)
                continue;

            var direction = obstacleTarget.position - origin;
            var distance = direction.magnitude;

            if (distance <= 0.0001f)
                continue;

            var angle = Vector3.Angle(transform.forward, direction);
            if (angle > fieldOfView * 0.5f)
                continue;

            if (!HasLineOfSight(origin, direction, distance, obstacleTarget))
            {
                DrawDebugRay(origin, obstacleTarget.position, debugBlockedColor);
                continue;
            }

            _ = seen.Add(characterHealth);

            var id = targetRoot ? targetRoot.GetInstanceID().ToString() : string.Empty;
            var toolbelt = characterHealth.GetComponentInChildren<ToolbeltNetworked>(true);
            var equipped = toolbelt ? toolbelt.CurrentEquippedObject : null;
            var factionId = TryGetFactionId(targetRoot);
            var facingDirection = ResolveFacingDirection(motor, targetRoot);
            var stance = motor ? (TopDownMotor.Stance?)motor.CurrentStance : null;
            var observation = Observation.ForCharacter(targetRoot, characterHealth.gameObject, id, characterHealth.Health, equipped, factionId, facingDirection, stance, BeliefSource.Sight, 1f, Time.time);
            buffer.Add(observation);
            lastObservations.Add(observation);

            DrawDebugRay(origin, obstacleTarget.position, debugHitColor);
        }

        return new List<Observation>(buffer);
    }

    private bool HasLineOfSight(Vector3 origin, Vector3 direction, float distance, Transform target)
    {
        if (!Physics.Raycast(origin, direction.normalized, out RaycastHit hit, distance, obstacleLayers, QueryTriggerInteraction.Ignore))
            return true;

        var hitRoot = hit.transform.root;
        var targetRoot = target.root;

        return hitRoot == targetRoot;
    }

    private string TryGetFactionId(Transform targetRoot)
    {
        if (!targetRoot)
            return null;

        var characterData = targetRoot.GetComponentInParent<CharacterData>();
        return characterData && characterData.Faction ? characterData.Faction.GetInstanceID().ToString() : null;
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

    private static Vector3? ResolveFacingDirection(TopDownMotor motor, Transform fallback)
    {
        if (motor)
        {
            var origin = motor.transform ? motor.transform.position : (Vector3?)null;
            if (motor.HasCursorTarget && origin.HasValue)
            {
                var elevatedTarget = motor.PlayerTarget + Vector3.up * 1.5f;
                var direction = elevatedTarget - origin.Value;
                if (direction.sqrMagnitude > 0.0001f)
                    return direction;
            }

            if (motor.transform)
                return motor.transform.forward;
        }

        return fallback ? (Vector3?)fallback.forward : null;
    }

    private static Transform ResolvePositionRoot(CharacterHealth characterHealth, TopDownMotor motor)
    {
        if (motor)
        {
            var controller = motor.GetComponent<CharacterController>();
            if (controller)
                return controller.transform;
        }

        if (characterHealth)
        {
            if (characterHealth.OwnerRoot)
                return characterHealth.OwnerRoot;

            return characterHealth.transform;
        }

        return null;
    }
}
