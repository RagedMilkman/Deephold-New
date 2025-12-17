using System.Collections.Generic;
using UnityEngine;

public class Senses : MonoBehaviour
{
    [SerializeReference] private List<ISense> senses = new();

    public IReadOnlyList<ISense> SenseModules => senses;

    public List<Observation> GetObservations()
    {
        var observations = new List<Observation>();

        foreach (var sense in senses)
        {
            if (sense == null)
                continue;

            var sensed = sense.GetObservations();
            if (sensed != null)
                observations.AddRange(sensed);
        }

        return observations;
    }
}
