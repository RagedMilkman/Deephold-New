using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Observing;
using FishNet.Object;
using FishNet.Transporting;
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
    private bool _isServerActive;

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

        SubscribeToNetworkEvents();
        UpdateServerActiveState(IsServerContext());
    }

    private void OnEnable()
    {
        UpdateServerActiveState(IsServerContext());
    }

    private void OnDisable()
    {
        FactionsService.Instance?.UnregisterFaction(this);
    }

    private void Update()
    {
        if (!_isServerActive)
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
        if (!_isServerActive)
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
        if (!_isServerActive)
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

        CharacterData.EnsureHierarchyActive(spawned);
    }

    private bool IsServerContext()
    {
        // When no network manager is present, assume single-player and allow spawning.
        return _networkManager == null || _networkManager.IsServer;
    }

    private void SubscribeToNetworkEvents()
    {
        if (_networkManager == null)
            return;

        _networkManager.ServerManager.OnServerConnectionState += HandleServerConnectionState;
        _networkManager.ServerManager.OnRemoteConnectionState += HandleRemoteConnectionState;
    }

    private void UnsubscribeFromNetworkEvents()
    {
        if (_networkManager == null)
            return;

        _networkManager.ServerManager.OnServerConnectionState -= HandleServerConnectionState;
        _networkManager.ServerManager.OnRemoteConnectionState -= HandleRemoteConnectionState;
    }

    private void HandleServerConnectionState(ServerConnectionStateArgs args)
    {
        switch (args.ConnectionState)
        {
            case LocalConnectionState.Started:
                UpdateServerActiveState(true);
                break;
            case LocalConnectionState.Stopping:
            case LocalConnectionState.Stopped:
                UpdateServerActiveState(false);
                break;
        }
    }

    private void HandleRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        if (!_isServerActive)
            return;

        if (args.ConnectionState == RemoteConnectionState.Started)
            SyncExistingMembersToConnection(conn);
    }

    private void SyncExistingMembersToConnection(NetworkConnection conn)
    {
        if (conn == null || _networkManager == null || !_networkManager.IsServer)
            return;

        foreach (CharacterData member in _members)
        {
            if (member == null)
                continue;

            NetworkObject netObj = member.GetComponent<NetworkObject>();
            if (netObj == null)
                continue;

            if (!netObj.IsSpawned)
            {
                _networkManager.ServerManager.Spawn(netObj.gameObject);
            }

            _networkManager.ServerManager.ObserverManager.AddObserver(conn, netObj);
        }
    }

    private void UpdateServerActiveState(bool isActive)
    {
        if (_isServerActive == isActive)
            return;

        _isServerActive = isActive;

        if (_isServerActive)
        {
            FactionsService.Instance?.RegisterFaction(this);
        }
        else
        {
            FactionsService.Instance?.UnregisterFaction(this);
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromNetworkEvents();
    }
}
