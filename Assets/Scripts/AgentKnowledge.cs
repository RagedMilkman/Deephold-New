using System.Collections.Generic;
using UnityEngine;

public class AgentKnowledge : MonoBehaviour
{
    private readonly Dictionary<string, CharacterKnowledge> characters = new();
    [SerializeField] private CharacterData selfCharacter;

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
        var facingDirection = selfCharacter.transform ? (Vector3?)selfCharacter.transform.forward : null;
        var topDownMotor = selfCharacter.GetComponentInParent<TopDownMotor>(true);
        var stance = topDownMotor ? (TopDownMotor.Stance?)topDownMotor.CurrentStance : null;

        var selfObservation = Observation.ForCharacter(selfCharacter.transform, selfCharacter.gameObject, id, healthValue, equipped, factionId, facingDirection, stance, BeliefSource.Inferred, 1f, Time.time);
        Self.UpdateFromObservation(selfObservation);
    }
}
