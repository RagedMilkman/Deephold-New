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
    [SerializeField, Range(0f, 1f)] private float aggression = 0.5f;
    [SerializeField, Range(0f, 1f)] private float bravery = 0.5f;

    public float Aggression => aggression;
    public float Bravery => bravery;

    public IIntent CurrentIntent { get; private set; }

    private void Update()
    {
        if (!Senses || !Knowledge || !Intelligence)
            return;

        var observations = Senses.GetObservations();
        Knowledge.RecieveObservations(observations);

        CurrentIntent = Intelligence.ChooseIntent(Knowledge);
    }
}
