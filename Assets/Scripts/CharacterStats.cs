using UnityEngine;

/// <summary>
/// Basic container for character combat stats.
/// </summary>
public class CharacterStats : MonoBehaviour
{
    private const int MinLevel = 1;
    private const int MaxLevel = 10;

    [SerializeField, Range(MinLevel, MaxLevel)]
    private int shootingLevel = MinLevel;

    [SerializeField, Range(MinLevel, MaxLevel)]
    private int meleeLevel = MinLevel;

    /// <summary>
    /// Level for shooting-related actions.
    /// </summary>
    public int ShootingLevel
    {
        get => shootingLevel;
        set => shootingLevel = ClampLevel(value);
    }

    /// <summary>
    /// Level for melee-related actions.
    /// </summary>
    public int MeleeLevel
    {
        get => meleeLevel;
        set => meleeLevel = ClampLevel(value);
    }

    private int ClampLevel(int value)
    {
        return Mathf.Clamp(value, MinLevel, MaxLevel);
    }
}
