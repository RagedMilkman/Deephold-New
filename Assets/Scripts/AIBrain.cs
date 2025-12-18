using UnityEngine;

/// <summary>
/// Defines the core AI brain with references to its major subsystems.
/// </summary>
public class AIBrain : MonoBehaviour
{
    [field: SerializeField] public Senses Senses { get; private set; }
    [field: SerializeField] public AgentKnowledge Knowledge { get; private set; }
    [field: SerializeField] public AgentIntelligence Intelligence { get; private set; }
    [field: SerializeField] public GameObject Behaviours { get; private set; }

    [Header("Traits")]
    [SerializeField] private Personality personality = new();

    public Personality Personality => personality;

    public IIntent CurrentIntent { get; private set; }

    private void Update()
    {
        if (!Senses || !Knowledge || !Intelligence)
            return;

        var observations = Senses.GetObservations();
        Knowledge.RecieveObservations(observations);

        CurrentIntent = Intelligence.ChooseIntent(Knowledge, personality);
    }
}
