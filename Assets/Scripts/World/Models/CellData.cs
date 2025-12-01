using FishNet.Connection;
using System.Collections.Generic;

public enum CellType : byte { Empty, Solid, Diggable, EnemySpawner }

/// <summary>
/// Server-owned cell snapshot item. Includes its own grid coordinate.
/// </summary>
public struct CellData
{
    public int x;
    public int y;
    public CellType type;
    public short hp; // server-authoritative; clients can use for VFX
    [System.NonSerialized] public List<NetworkConnection> players;
    public bool hasMovementWeightOverride;
    public byte movementWeightOverride;

    public bool IsConsideredForPathfinding => type is CellType.Diggable or CellType.Empty;

    public byte? MovementWeight => type switch
    {
        CellType.Empty => 1,
        CellType.Diggable => hasMovementWeightOverride ? movementWeightOverride : (byte)10,
        _ => null
    };

    public bool ApplyMovementWeightOverride(byte newWeight)
    {
        if (type != CellType.Diggable)
            return ClearMovementWeightOverride();

        if (hasMovementWeightOverride && movementWeightOverride == newWeight)
            return false;

        hasMovementWeightOverride = true;
        movementWeightOverride = newWeight;
        return true;
    }

    public bool ClearMovementWeightOverride()
    {
        if (!hasMovementWeightOverride)
            return false;

        hasMovementWeightOverride = false;
        movementWeightOverride = 0;
        return true;
    }
}
