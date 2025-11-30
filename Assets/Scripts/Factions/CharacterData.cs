using UnityEngine;

/// <summary>
/// Stores faction-related information for a character.
/// Works for both player-controlled and AI-controlled characters.
/// </summary>
public sealed class CharacterData : MonoBehaviour
{
    [SerializeField, Tooltip("Optional identifier for the character.")] private string _characterId;
    [SerializeField, Tooltip("Faction this character currently belongs to.")] private FactionController _faction;

    /// <summary>
    /// Optional identifier for the character instance.
    /// </summary>
    public string CharacterId => _characterId;

    /// <summary>
    /// Faction this character is a member of, if any.
    /// </summary>
    public FactionController Faction => _faction;

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

    private void OnDestroy()
    {
        _faction?.RemoveCharacter(this);
    }
}
