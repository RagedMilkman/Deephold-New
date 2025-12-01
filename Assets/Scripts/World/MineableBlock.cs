using UnityEngine;

/// <summary>
/// Client-side visual for a mineable block. Has no health/state.
/// Its only job is to tell the server that it was hit for N at position P.
/// </summary>
public class MineableBlock : MonoBehaviour
{
    [SerializeField] bool invincible = false;

    /// <summary>
    /// Called by whatever weapon/tool hits this block.
    /// Runs on the client that performed the hit and forwards to the server.
    /// </summary>
    public void ReportHit(int amount)
    {
        if (invincible || amount <= 0)
            return;

        // Forward to server with world position (server will resolve to a cell)
        HitReporter.Instance?.Server_ReportHit(amount, transform.position);
    }

    public void SetInvincible(bool value) => invincible = value;
    public bool IsInvincible => invincible;


}
