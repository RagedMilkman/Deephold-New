using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Selects and ticks the appropriate behaviour for the AI brain's current intent.
/// </summary>
public class BehaviourModule : MonoBehaviour
{
    [SerializeField] private List<BehaviourBase> behaviours = new();

    public BehaviourBase ActiveBehaviour { get; private set; }
    public IReadOnlyList<BehaviourBase> Behaviours => behaviours;

    /// <summary>
    /// Run once per frame to keep the active behaviour in sync with the current intent.
    /// </summary>
    public void Tick(IIntent currentIntent)
    {
        if (currentIntent == null)
        {
            StopActiveBehaviour();
            return;
        }

        if (ActiveBehaviour == null || ActiveBehaviour.IntentType != currentIntent.Type)
            SwitchBehaviour(currentIntent);

        ActiveBehaviour?.TickBehaviour(currentIntent);
    }

    private void SwitchBehaviour(IIntent intent)
    {
        StopActiveBehaviour();

        if (behaviours == null)
            return;

        foreach (var behaviour in behaviours)
        {
            if (!behaviour || behaviour.IntentType != intent.Type)
                continue;

            ActiveBehaviour = behaviour;
            ActiveBehaviour.BeginBehaviour(intent);
            break;
        }
    }

    private void StopActiveBehaviour()
    {
        if (!ActiveBehaviour)
            return;

        ActiveBehaviour.EndBehaviour();
        ActiveBehaviour = null;
    }
}
