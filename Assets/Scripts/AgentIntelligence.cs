using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Decides which intent the agent should act on based on its considerations.
/// </summary>
public class AgentIntelligence : MonoBehaviour
{
    [SerializeField] private List<Consideration> considerations = new();

    public IReadOnlyList<Consideration> Considerations => considerations;
    public IIntent LastIntent { get; private set; }

    /// <summary>
    /// Chooses the highest urgency intent produced by attached considerations.
    /// </summary>
    /// <param name="knowledge">The agent's current knowledge.</param>
    /// <param name="personality">The agent's personality traits.</param>
    /// <returns>The chosen intent, or null if no valid intent was produced.</returns>
    public IIntent ChooseIntent(AgentKnowledge knowledge, Personality personality)
    {
        if (knowledge == null || considerations == null || considerations.Count == 0)
            return null;

        IIntent bestIntent = null;

        foreach (var consideration in considerations)
        {
            if (!consideration)
                continue;

            var intent = consideration.EvaluateIntent(knowledge, personality);
            if (intent == null)
                continue;

            if (bestIntent == null || intent.Urgency > bestIntent.Urgency)
                bestIntent = intent;
        }

        LastIntent = bestIntent;
        return bestIntent;
    }
}
