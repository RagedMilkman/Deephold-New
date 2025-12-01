using FishNet.Object;
using System;
using UnityEngine;
using UnityEngine.Analytics;

public class PlayerData : NetworkBehaviour
{
    [SerializeField] string displayName = "Player";
    [SerializeField] int classId = 0;  // extend later
    [SerializeField] Color color = Color.white;
    [SerializeField] int Gender = 0;

    public event Action<int> GenderChanged;

    public string DisplayName => displayName;
    public int ClassId => classId;
    public Color Color => color;
    public int GenderId => Gender;

    // Server sets and broadcasts
    public void ServerSetIdentity(string name, int clsId = 0, Color? tint = null, int gender = 0)
    {
        if (!IsServer)
            return;

        displayName = string.IsNullOrWhiteSpace(name)
            ? "Player"
            : name.Trim();

        classId = clsId;
        color = tint ?? Color.white;
        Gender = gender;
        GenderChanged?.Invoke(Gender);
        RPC_Identity(displayName, classId, color, gender);
    }

    [ObserversRpc]
    void RPC_Identity(string name, int clsId, Color tint, int gender)
    {
        displayName = name;
        classId = clsId;
        Gender = gender;
        GenderChanged?.Invoke(Gender);
        // TODO: update nameplate/roster UI here if you like
    }
}
