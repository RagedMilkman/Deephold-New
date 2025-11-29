using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks members, funds, and spawning for a single faction.
/// </summary>
public sealed class FactionController : MonoBehaviour
{
    [Header("Economy")]
    [SerializeField, Tooltip("Currency generated per second.")] private float _passiveIncomePerSecond = 1f;
    [SerializeField, Tooltip("Starting balance for this faction.")] private float _startingFunds = 0f;

    [Header("Spawning")]
    [SerializeField, Tooltip("Prefab spawned for new faction members.")] private GameObject _characterPrefab;
    [SerializeField, Tooltip("Spawn points available for this faction.")] private List<Transform> _spawnPoints = new();

    private readonly List<CharacterData> _members = new();
    private float _currentFunds;
    private int _nextSpawnIndex;

    public IReadOnlyList<CharacterData> Members => _members;
    public float CurrentFunds => _currentFunds;
    public float PassiveIncomePerSecond => _passiveIncomePerSecond;

    private void Awake()
    {
        _currentFunds = _startingFunds;
    }

    private void OnEnable()
    {
        FactionsService.Instance?.RegisterFaction(this);
    }

    private void OnDisable()
    {
        FactionsService.Instance?.UnregisterFaction(this);
    }

    private void Update()
    {
        AccruePassiveIncome(Time.deltaTime);
    }

    public void AccruePassiveIncome(float deltaTime)
    {
        if (deltaTime <= 0f || _passiveIncomePerSecond <= 0f)
            return;

        _currentFunds += _passiveIncomePerSecond * deltaTime;
    }

    public void Deposit(float amount)
    {
        if (amount <= 0f)
            return;

        _currentFunds += amount;
    }

    public bool Withdraw(float amount)
    {
        if (amount <= 0f || amount > _currentFunds)
            return false;

        _currentFunds -= amount;
        return true;
    }

    public void AddCharacter(CharacterData character)
    {
        if (character == null || _members.Contains(character))
            return;

        _members.Add(character);
        character.SetFactionInternal(this);
    }

    public void RemoveCharacter(CharacterData character)
    {
        if (character == null || !_members.Remove(character))
            return;

        character.SetFactionInternal(null);
    }

    public CharacterData SpawnCharacter()
    {
        if (_characterPrefab == null)
        {
            Debug.LogWarning($"[{nameof(FactionController)}] No character prefab set for faction on {name}.");
            return null;
        }

        if (_spawnPoints.Count == 0)
        {
            Debug.LogWarning($"[{nameof(FactionController)}] No spawn points configured for faction on {name}.");
            return null;
        }

        Transform spawnPoint = _spawnPoints[_nextSpawnIndex % _spawnPoints.Count];
        _nextSpawnIndex++;

        GameObject spawned = Instantiate(_characterPrefab, spawnPoint.position, spawnPoint.rotation);
        CharacterData character = spawned.GetComponent<CharacterData>();
        if (character == null)
            character = spawned.AddComponent<CharacterData>();

        AddCharacter(character);
        return character;
    }
}
