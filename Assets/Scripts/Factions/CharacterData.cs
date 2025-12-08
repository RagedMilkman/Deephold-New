using UnityEngine;

/// <summary>
/// Stores faction-related information for a character.
/// Works for both player-controlled and AI-controlled characters.
/// </summary>
public sealed class CharacterData : MonoBehaviour
{
    [SerializeField, Tooltip("Optional identifier for the character.")] private string _characterId;
    [SerializeField, Tooltip("Faction this character currently belongs to.")] private FactionController _faction;
    [SerializeField, Tooltip("Force this character prefab hierarchy to activate on spawn (useful if the prefab is saved inactive).")]
    private bool _forceEnableHierarchy = true;
    [SerializeField, Tooltip("Health component responsible for routing damage from hitboxes.")] private CharacterHealth _health;

    /// <summary>
    /// Optional identifier for the character instance.
    /// </summary>
    public string CharacterId => _characterId;

    /// <summary>
    /// Faction this character is a member of, if any.
    /// </summary>
    public FactionController Faction => _faction;

    /// <summary>
    /// Health handler for this character, if present.
    /// </summary>
    public CharacterHealth Health => _health;

    private void Awake()
    {
        if (_forceEnableHierarchy)
            EnsureHierarchyActive(gameObject);

        CacheHealth();
    }

    /// <summary>
    /// Assigns the character to the provided faction.
    /// Handles deregistering from the previous faction if necessary.
    /// </summary>
    /// <param name="faction">Faction to join.</param>
    public void AssignToFaction(FactionController faction)
    {
        if (_faction == faction)
            return;

        _faction?.RemoveCharacter(this);
        faction?.AddCharacter(this);
    }

    internal void SetFactionInternal(FactionController faction)
    {
        _faction = faction;
    }

    internal static void EnsureHierarchyActive(GameObject root)
    {
        if (root == null)
            return;

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!t.gameObject.activeSelf)
                t.gameObject.SetActive(true);
        }
    }

    private void OnDestroy()
    {
        _faction?.RemoveCharacter(this);
    }

    private void CacheHealth()
    {
        if (!_health)
            _health = GetComponent<CharacterHealth>();

        _health?.RefreshHitBoxes();
    }
}
