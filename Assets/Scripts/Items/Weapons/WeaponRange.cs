using System;

/// <summary>
/// Represents effective distance ranges for a weapon.
/// </summary>
[Serializable]
public struct WeaponRange
{
    public float minCapableDistance;
    public float minPreferredDistance;
    public float preferredDistance;
    public float maxPreferredDistance;
    public float maxCapableDistance;

    public WeaponRange(
        float minCapableDistance,
        float minPreferredDistance,
        float preferredDistance,
        float maxPreferredDistance,
        float maxCapableDistance)
    {
        this.minCapableDistance = minCapableDistance;
        this.minPreferredDistance = minPreferredDistance;
        this.preferredDistance = preferredDistance;
        this.maxPreferredDistance = maxPreferredDistance;
        this.maxCapableDistance = maxCapableDistance;
    }
}
