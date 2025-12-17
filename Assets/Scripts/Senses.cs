using System.Collections.Generic;
using UnityEngine;

public class Senses : MonoBehaviour
{
    [Tooltip("GameObjects that contain one or more components implementing ISense.")]
    [SerializeField] private List<GameObject> senseObjects = new();

    public IReadOnlyList<GameObject> SenseObjects => senseObjects;

    public List<Observation> GetObservations()
    {
        var observations = new List<Observation>();
        var seenSenses = new HashSet<ISense>();

        foreach (var obj in senseObjects)
        {
            if (!obj)
                continue;

            var senseComponents = obj.GetComponents<ISense>();
            if (senseComponents == null)
                continue;

            foreach (var sense in senseComponents)
            {
                if (sense == null || seenSenses.Contains(sense))
                    continue;

                seenSenses.Add(sense);

                var sensed = sense.GetObservations();
                if (sensed != null)
                    observations.AddRange(sensed);
            }
        }

        return observations;
    }
}
