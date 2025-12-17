using System.Collections.Generic;
using UnityEngine;

public class AgentKnowledge : MonoBehaviour
{
    private readonly Dictionary<BeliefKey, Belief> beliefs = new();

    public IReadOnlyCollection<Belief> Beliefs => beliefs.Values;

    public void RecieveObservations(List<Observation> observations)
    {
        if (observations == null || observations.Count == 0)
            return;

        foreach (var observation in observations)
        {
            if (observation == null)
                continue;

            switch (observation.Type)
            {
                case ObservationType.Object:
                    AddOrUpdateBelief(CreateObjectBelief(observation));
                    break;
                case ObservationType.Event:
                    AddOrUpdateBelief(CreateEventBelief(observation));
                    break;
            }
        }
    }

    private Belief CreateObjectBelief(Observation observation)
    {
        var target = observation.ObservedObject;
        var position = observation.Location ? observation.Location.position : Vector3.zero;
        var subject = InferSubject(target);

        return new Belief(
            subject,
            BeliefProposition.Position,
            position,
            1f,
            observation.Source,
            target);
    }

    private Belief CreateEventBelief(Observation observation)
    {
        var subject = BeliefSubject.Unknown;
        var position = observation.Location ? observation.Location.position : Vector3.zero;

        return new Belief(
            subject,
            BeliefProposition.Event,
            position,
            1f,
            observation.Source,
            observation.ObservedObject);
    }

    private void AddOrUpdateBelief(Belief belief)
    {
        if (belief == null)
            return;

        var key = new BeliefKey(belief.Target, belief.Proposition);
        beliefs[key] = belief;
    }

    private static BeliefSubject InferSubject(GameObject target)
    {
        if (!target)
            return BeliefSubject.Unknown;

        if (target.CompareTag("Enemy"))
            return BeliefSubject.Enemy;
        if (target.CompareTag("Player") || target.CompareTag("Ally"))
            return BeliefSubject.Ally;

        return BeliefSubject.Unknown;
    }

    private readonly struct BeliefKey
    {
        private readonly GameObject target;
        private readonly BeliefProposition proposition;

        public BeliefKey(GameObject target, BeliefProposition proposition)
        {
            this.target = target;
            this.proposition = proposition;
        }

        public override bool Equals(object obj)
        {
            return obj is BeliefKey other &&
                   ReferenceEquals(target, other.target) &&
                   proposition == other.proposition;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + (target ? target.GetHashCode() : 0);
                hash = hash * 23 + proposition.GetHashCode();
                return hash;
            }
        }
    }
}
