using System.Collections.Generic;
using UnityEngine;

public class AgentKnowledge : MonoBehaviour
{
    private readonly Dictionary<string, CharacterKnowledge> characters = new();
    [SerializeField] private CharacterData selfCharacter;
    [SerializeField, Min(0f)] private float knowledgeRetentionSeconds = 15f;
    [SerializeField, Min(0f)] private float defaultConfidenceHalfLifeSeconds = 10f;
    [SerializeField] private bool drawKnowledgeGizmos = false;
    [SerializeField] private Color selfKnowledgeColor = new(0.25f, 0.9f, 0.65f, 0.5f);
    [SerializeField] private Color knownCharacterColor = new(1f, 0.75f, 0.1f, 0.5f);

    public IReadOnlyDictionary<string, CharacterKnowledge> Characters => characters;
    public CharacterKnowledge Self { get; private set; }

    private void Awake()
    {
        if (!selfCharacter)
            selfCharacter = GetComponentInParent<CharacterData>();

        UpdateSelfKnowledge();
    }

    public void RecieveObservations(List<Observation> observations)
    {
        UpdateSelfKnowledge();
        DecayKnownCharacters();

        if (observations == null || observations.Count == 0)
            return;

        foreach (var observation in observations)
        {
            if (observation == null)
                continue;

            if (observation.Type == ObservationType.Character)
                AddOrUpdateCharacterKnowledge(observation);
        }
    }

    private void AddOrUpdateCharacterKnowledge(Observation observation)
    {
        var id = !string.IsNullOrWhiteSpace(observation.CharacterData.Id)
            ? observation.CharacterData.Id
            : observation.ObservedObject ? observation.ObservedObject.GetInstanceID().ToString() : null;

        if (string.IsNullOrWhiteSpace(id))
            return;

        if (!characters.TryGetValue(id, out var knowledge))
        {
            knowledge = new CharacterKnowledge(id, observation.ObservedObject);
            characters.Add(id, knowledge);
        }

        knowledge.UpdateFromObservation(observation);
    }

    private void DecayKnownCharacters()
    {
        if (knowledgeRetentionSeconds <= 0f && defaultConfidenceHalfLifeSeconds <= 0f)
            return;

        if (characters.Count == 0)
            return;

        var currentTime = Time.time;
        var idsToForget = new List<string>();

        foreach (var kvp in characters)
        {
            kvp.Value.ApplyDecay(currentTime, knowledgeRetentionSeconds, defaultConfidenceHalfLifeSeconds);
            if (!kvp.Value.HasAnyBeliefs)
                idsToForget.Add(kvp.Key);
        }

        foreach (var id in idsToForget)
            characters.Remove(id);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawKnowledgeGizmos)
            return;

        if (Self != null)
            DrawCharacterKnowledge(Self, selfKnowledgeColor);

        foreach (var kvp in characters)
        {
            if (kvp.Value != null)
                DrawCharacterKnowledge(kvp.Value, knownCharacterColor);
        }
    }

    private static void DrawCharacterKnowledge(CharacterKnowledge knowledge, Color baseColor)
    {
        if (!knowledge.Position.HasValue)
            return;

        var positionBelief = knowledge.Position.Value;
        var confidence = Mathf.Clamp01(positionBelief.Confidence);
        var radius = Mathf.Lerp(0.15f, 0.45f, confidence);
        var fadedColor = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * 0.6f);

        Gizmos.color = baseColor;
        Gizmos.DrawSphere(positionBelief.Value, radius);

        Gizmos.color = fadedColor;
        Gizmos.DrawWireSphere(positionBelief.Value, radius + 0.05f);

        if (!knowledge.FacingDirection.HasValue)
            return;

        var facingBelief = knowledge.FacingDirection.Value;
        var direction = facingBelief.Value.sqrMagnitude > 0.0001f ? facingBelief.Value.normalized : Vector3.forward;
        var facingLength = Mathf.Lerp(0.5f, 2f, Mathf.Clamp01(facingBelief.Confidence));

        Gizmos.DrawLine(positionBelief.Value, positionBelief.Value + direction * facingLength);
    }

    private void UpdateSelfKnowledge()
    {
        if (!selfCharacter)
            return;

        var id = !string.IsNullOrWhiteSpace(selfCharacter.CharacterId)
            ? selfCharacter.CharacterId
            : selfCharacter.gameObject.GetInstanceID().ToString();

        if (string.IsNullOrWhiteSpace(id))
            return;

        if (Self == null || Self.Id != id)
            Self = new CharacterKnowledge(id, selfCharacter.gameObject);

        var healthValue = selfCharacter.Health ? (float?)selfCharacter.Health.Health : null;
        var toolbelt = selfCharacter.GetComponentInChildren<ToolbeltNetworked>(true);
        var equipped = toolbelt ? toolbelt.CurrentEquippedObject : null;
        var factionId = selfCharacter.Faction ? selfCharacter.Faction.GetInstanceID().ToString() : null;
        var topDownMotor = selfCharacter.GetComponentInParent<TopDownMotor>(true);
        var facingDirection = ResolveFacingDirection(topDownMotor, selfCharacter.transform);
        var stance = topDownMotor ? (TopDownMotor.Stance?)topDownMotor.CurrentStance : null;

        var selfObservation = Observation.ForCharacter(selfCharacter.transform, selfCharacter.gameObject, id, healthValue, equipped, factionId, facingDirection, stance, BeliefSource.Inferred, 1f, Time.time);
        Self.UpdateFromObservation(selfObservation);
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
}
