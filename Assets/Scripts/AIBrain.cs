using UnityEngine;

/// <summary>
/// Defines the core AI brain with references to its major subsystems.
/// </summary>
public class AIBrain : MonoBehaviour
{
    [field: SerializeField] public Senses Senses { get; private set; }
    [field: SerializeField] public GameObject Knowledge { get; private set; }
    [field: SerializeField] public GameObject Intelligence { get; private set; }
    [field: SerializeField] public GameObject Behaviours { get; private set; }
}
