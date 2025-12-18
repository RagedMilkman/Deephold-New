using UnityEngine;

public abstract class Consideration : MonoBehaviour
{
    /// <summary>
    /// Produces an intent based on the provided knowledge.
    /// </summary>
    /// <param name="knowledge">The knowledge the agent currently has.</param>
    /// <returns>An intent suggestion or null if this consideration has no opinion.</returns>
    public abstract IIntent EvaluateIntent(AgentKnowledge knowledge);
}
