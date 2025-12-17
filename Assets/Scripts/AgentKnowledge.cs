using System.Collections.Generic;
using UnityEngine;

public class AgentKnowledge : MonoBehaviour
{
    private readonly Dictionary<string, CharacterKnowledge> characters = new();

    public IReadOnlyDictionary<string, CharacterKnowledge> Characters => characters;

    public void RecieveObservations(List<Observation> observations)
    {
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
}
