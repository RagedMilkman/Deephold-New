using System;

/// <summary>
/// Represents effective distance ranges for a weapon.
/// </summary>
[Serializable]
public struct WeaponRange
{
    public float minDistance;
    public float preferredDistance;
    public float maxDistance;

    public WeaponRange(float minDistance, float preferredDistance, float maxDistance)
    {
        this.minDistance = minDistance;
        this.preferredDistance = preferredDistance;
        this.maxDistance = maxDistance;
    }
}
