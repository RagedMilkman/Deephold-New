// BlockGenerator.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns client-side cell cache and ALL logic for spawning/despawning blocks.
/// </summary>
public class BlockGenerator : MonoBehaviour
{
    [Header("Visuals")]
    [SerializeField] private GameObject blockPrefab;
    [SerializeField] private Transform blocksParent;
    [SerializeField] private Material floorMaterial;
    [SerializeField] private GameObject blockMaskPrefab;
    [SerializeField] private Transform masksParent;
    [SerializeField] private float maskHeightOffset = 0.01f;

    // World meta
    int width, height;
    float cellSize = 1f;

    // Client snapshot (by coordinate)
    readonly Dictionary<Vector2Int, CellData> cells = new();
    readonly Dictionary<Vector2Int, GameObject> blocksByCell = new();
    readonly Dictionary<Vector2Int, GameObject> masksByCell = new();

    float blockHalfHeight;
    bool floorBuilt;

    void Awake()
    {
        if (!blockPrefab)
        {
            Debug.LogError("BlockGenerator missing blockPrefab");
            enabled = false;
            return;
        }
        blockHalfHeight = blockPrefab.transform.localScale.y * 0.5f;
    }

    // ----- Director -> BlockGenerator API -----

    public void SetWorldMeta(int w, int h, float size)
    {
        width = w; height = h; cellSize = Mathf.Max(0.0001f, size);

        if (!floorBuilt)
        {
            BuildFloor();
            floorBuilt = true;
        }
    }

    /// <summary>Replace the whole cache and rebuild visuals.</summary>
    public void SyncAll(CellData[] snapshot)
    {
        // clear existing
        foreach (var go in blocksByCell.Values) if (go) Destroy(go);
        blocksByCell.Clear();
        foreach (var go in masksByCell.Values) if (go) Destroy(go);
        masksByCell.Clear();
        cells.Clear();

        if (snapshot != null)
            foreach (var c in snapshot)
                cells[new Vector2Int(c.x, c.y)] = c;

        // rebuild exposed-only
        BuildExposedFor(AllCoords());
    }

    /// <summary>Apply incremental changes and update visuals around them.</summary>
    public void SyncChanges(CellData[] deltas)
    {
        if (deltas == null || deltas.Length == 0) return;

        var dirty = new HashSet<Vector2Int>();
        foreach (var d in deltas)
        {
            var key = new Vector2Int(d.x, d.y);
            cells[key] = d; // upsert
            dirty.Add(key);
            foreach (var n in Neighbors4(key)) dirty.Add(n);
        }

        BuildExposedFor(dirty);
    }

    // ----- Visual building -----

    void BuildFloor()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
        go.name = "Floor";
        if (floorMaterial) go.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;

        float worldW = Mathf.Max(1, width) * cellSize;
        float worldH = Mathf.Max(1, height) * cellSize;
        go.transform.localScale = new Vector3(worldW / 10f, 1f, worldH / 10f);
        go.transform.position = new Vector3(worldW * 0.5f, 0f, worldH * 0.5f);
        go.layer = 3;
    }

    void BuildExposedFor(IEnumerable<Vector2Int> coords)
    {
        // Cleanup any nulls in mapping
        TempCleanNulls();

        foreach (var p in coords)
        {
            if (!In(p)) continue;

            if (!cells.TryGetValue(p, out var data))
            {
                // Unknown cell -> ensure despawn (safety)
                TryDespawn(p);
                TryDespawnMask(p);
                continue;
            }

            if (!IsBlockType(data.type))
            {
                // Not a block type
                TryDespawn(p);
                TryDespawnMask(p);
                continue;
            }

            if (data.type != CellType.EnemySpawner && !IsExposed(p))
            {
                TryDespawn(p);
                TrySpawnMask(p);
                continue;
            }

            TryDespawnMask(p);

            // Spawn if missing
            if (!blocksByCell.TryGetValue(p, out var go) || go == null)
            {
                var world = CellCenter(p);
                world.y = blockHalfHeight;

                go = Instantiate(blockPrefab, world, Quaternion.identity, blocksParent ? blocksParent : transform);
                blocksByCell[p] = go;

                var block = go.GetComponent<MineableBlock>();
                if (block) block.SetInvincible(IsInvincible(data.type));
            }
        }
    }

    void TryDespawn(Vector2Int p)
    {
        if (blocksByCell.TryGetValue(p, out var go) && go) Destroy(go);
        blocksByCell.Remove(p);
    }

    void TrySpawnMask(Vector2Int p)
    {
        if (!blockMaskPrefab) return;

        if (!masksByCell.TryGetValue(p, out var go) || go == null)
        {
            var world = CellCenter(p);
            world.y = blockHalfHeight * 2f + maskHeightOffset;

            go = Instantiate(blockMaskPrefab, world, Quaternion.identity, masksParent ? masksParent : transform);
            masksByCell[p] = go;
        }
    }

    void TryDespawnMask(Vector2Int p)
    {
        if (masksByCell.TryGetValue(p, out var go) && go) Destroy(go);
        masksByCell.Remove(p);
    }

    void TempCleanNulls()
    {
        CleanNulls(blocksByCell);
        CleanNulls(masksByCell);
    }

    static void CleanNulls(Dictionary<Vector2Int, GameObject> map)
    {
        if (map.Count == 0) return;

        List<Vector2Int> forget = null;
        foreach (var kv in map)
        {
            if (kv.Value == null) (forget ??= new()).Add(kv.Key);
        }
        if (forget != null)
            foreach (var k in forget)
                map.Remove(k);
    }

    // ----- Helpers -----

    IEnumerable<Vector2Int> AllCoords()
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                yield return new Vector2Int(x, y);
    }

    IEnumerable<Vector2Int> Neighbors4(Vector2Int p)
    {
        yield return new Vector2Int(p.x - 1, p.y);
        yield return new Vector2Int(p.x + 1, p.y);
        yield return new Vector2Int(p.x, p.y - 1);
        yield return new Vector2Int(p.x, p.y + 1);
    }

    bool In(Vector2Int p) => (uint)p.x < (uint)width && (uint)p.y < (uint)height;

    bool IsEmpty(Vector2Int p)
        => !cells.TryGetValue(p, out var c) || c.type == CellType.Empty;

    static bool IsBlockType(CellType type)
        => type == CellType.Diggable || type == CellType.Solid || type == CellType.EnemySpawner;

    static bool IsInvincible(CellType type)
        => type == CellType.Solid || type == CellType.EnemySpawner;

    bool IsExposed(Vector2Int p)
    {
        if (!In(p) || !cells.TryGetValue(p, out var c)) return false;
        if (!IsBlockType(c.type)) return false;
        return IsEmpty(new Vector2Int(p.x - 1, p.y))
            || IsEmpty(new Vector2Int(p.x + 1, p.y))
            || IsEmpty(new Vector2Int(p.x, p.y - 1))
            || IsEmpty(new Vector2Int(p.x, p.y + 1));
    }

    Vector3 CellCenter(Vector2Int p)
        => new Vector3((p.x + 0.5f) * cellSize, 0f, (p.y + 0.5f) * cellSize);
}
