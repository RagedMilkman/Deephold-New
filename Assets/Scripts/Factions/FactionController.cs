using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Tracks members, funds, and spawning for a single faction.
/// </summary>
public sealed class FactionController : MonoBehaviour
{
    [Header("Economy")]
    [SerializeField, Tooltip("Currency generated per second.")] private float _passiveIncomePerSecond = 1f;
    [SerializeField, Tooltip("Starting balance for this faction.")] private float _startingFunds = 0f;
    [SerializeField, Tooltip("Cost to spawn a single character from this faction.")] private float _characterPrice = 0f;

    [Header("Spawning")]
    [SerializeField, Tooltip("Prefab spawned for new faction members (must include a NetworkObject).")] private GameObject _characterPrefab;
    [SerializeField, Tooltip("Spawn points available for this faction.")] private List<Transform> _spawnPoints = new();
    [SerializeField, Min(1), Tooltip("Maximum active characters this faction can maintain.")] private int _maxActiveCharacters = 5;
    [SerializeField, Tooltip("Automatically purchase and spawn characters when affordable.")] private bool _autoPurchase = true;
    [SerializeField, Tooltip("Force spawned prefabs and their nested children to become active.")] private bool _forceEnableSpawnHierarchy = true;

    [Header("Networking")]
    [SerializeField, Tooltip("Network manager used to spawn characters on the server.")] private NetworkManager _networkManager;

    private readonly List<CharacterData> _members = new();
    private float _currentFunds;
    private int _nextSpawnIndex;

    public IReadOnlyList<CharacterData> Members => _members;
    public float CurrentFunds => _currentFunds;
    public float PassiveIncomePerSecond => _passiveIncomePerSecond;
    public int MaxActiveCharacters => _maxActiveCharacters;
    public float CharacterPrice => _characterPrice;

    private void Awake()
    {
        _currentFunds = _startingFunds;

        if (_networkManager == null)
            _networkManager = FindObjectOfType<NetworkManager>();
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
        if (!IsServerContext())
            return;

        AccruePassiveIncome(Time.deltaTime);
        if (_autoPurchase)
            TryPurchaseAndSpawn();
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

    public bool TryPurchaseAndSpawn()
    {
        if (!IsServerContext())
            return false;

        if (_maxActiveCharacters > 0 && _members.Count >= _maxActiveCharacters)
            return false;

        float price = Mathf.Max(0f, _characterPrice);
        if (price > 0f && _currentFunds < price)
            return false;

        if (price > 0f && !Withdraw(price))
            return false;

        return SpawnCharacter() != null;
    }

    public void AddCharacter(CharacterData character)
    {
        if (character == null || _members.Contains(character))
            return;

        if (_maxActiveCharacters > 0 && _members.Count >= _maxActiveCharacters)
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
        if (!IsServerContext())
            return null;

        if (_maxActiveCharacters > 0 && _members.Count >= _maxActiveCharacters)
            return null;

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
        EnsureHierarchyActive(spawned);
        NetworkObject netObj = spawned.GetComponent<NetworkObject>();
        if (netObj != null && _networkManager != null && _networkManager.IsServer)
            _networkManager.ServerManager.Spawn(spawned);

        CharacterData character = spawned.GetComponent<CharacterData>();
        if (character == null)
            character = spawned.AddComponent<CharacterData>();

        AddCharacter(character);
        return character;
    }

    private void EnsureHierarchyActive(GameObject spawned)
    {
        if (!_forceEnableSpawnHierarchy || spawned == null)
            return;

        foreach (Transform t in spawned.GetComponentsInChildren<Transform>(true))
        {
            if (!t.gameObject.activeSelf)
                t.gameObject.SetActive(true);
        }
    }

    private bool IsServerContext()
    {
        // When no network manager is present, assume single-player and allow spawning.
        return _networkManager == null || _networkManager.IsServer;
    }
}
