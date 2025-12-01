using FishNet.Object;
using System;
using UnityEngine;

[DefaultExecutionOrder(ServiceExecutionOrder.HitReporter)]
public class HitReporter : NetworkBehaviour
{
    public static HitReporter Instance { get; private set; }
    void Awake() => Instance = this;

    // ---------- Networking (wire these to your netcode) ----------

    /// <summary>
    /// Server RPC: the client calls this to request a hit be applied.
    /// Implement with your networking layer (e.g., PurrNet [ServerRpc]).
    /// </summary>
    [ServerRpc]
    public void Server_ReportHit(int amount, Vector3 atWorldPos)
    {
        if (!IsServer)
        {
            // In a real implementation, this method body would be empty and decorated with a [ServerRpc].
            // For editor playmode without net, we can early out.
            return;
        }

        // On the server, resolve and apply through the authoritative grid service.
        var grid = FindFirstObjectByType<GridDirector>();
        if (!grid) return;
        grid.ServerApplyHitAtWorld(atWorldPos, amount);
    }
}