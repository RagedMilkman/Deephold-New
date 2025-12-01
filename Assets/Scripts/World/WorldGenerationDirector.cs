// WorldGenerationDirector.cs
using UnityEngine;

/// <summary>
/// Thin façade for GridService -> client. Only receives snapshots/updates
/// and forwards to BlockGenerator. Keeps the world meta (size) separate from cells.
/// </summary>
public class WorldGenerationDirector : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private BlockGenerator blockGenerator;

    private int width;
    private int height;
    private float cellSize = 1f;

    void Awake()
    {
        if (!blockGenerator) blockGenerator = GetComponentInChildren<BlockGenerator>();
        if (!blockGenerator) Debug.LogError("WorldGenerationDirector needs a BlockGenerator");
    }

    // ----- Called by the server via your networking layer -----

    /// <summary>Set or update world metadata (must be called before InitBlocks/UpdateBlocks).</summary>
    public void SetWorldMeta(int w, int h, float size)
    {
        width = w; 
        height = h; 
        cellSize = size;
        blockGenerator?.SetWorldMeta(w, h, size);
    }

    /// <summary>
    /// Full snapshot after connect (or major reset). Rebuilds visuals.
    /// </summary>
    public void InitBlocks(CellData[] snapshot)
    {
        if (!blockGenerator) return;
        blockGenerator.SyncAll(snapshot);
    }

    /// <summary>
    /// Incremental changes (mined cells, streamed edits, etc).
    /// Only includes modified cells.
    /// </summary>
    public void UpdateBlocks(CellData[] deltas)
    {
        if (!blockGenerator) return;
        blockGenerator.SyncChanges(deltas);
    }
}
