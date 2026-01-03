using UnityEngine;

/// <summary>
/// Combat- and interaction-oriented actions that orchestrate toolbelt and motor actuators.
/// </summary>
public class CombatActions : MonoBehaviour
{
    [SerializeField] private MotorActuator motorActuator;
    [SerializeField] private ToolbeltActuator toolbeltActuator;

    public IWeapon EquippedWeapon => toolbeltActuator ? toolbeltActuator.EquippedWeapon : null;

    private void Awake()
    {
        if (!motorActuator)
            motorActuator = GetComponentInChildren<MotorActuator>();

        if (!toolbeltActuator)
            toolbeltActuator = GetComponentInChildren<ToolbeltActuator>();
    }

    /// <summary>
    /// Switch between active and passive stances.
    /// </summary>
    public void SetActiveStance(bool active)
    {
        if (toolbeltActuator)
            toolbeltActuator.SetActiveStance(active);
    }

    /// <summary>
    /// Aim toward the target and trigger the equipped item once facing within <paramref name="facingThresholdDegrees"/>.
    /// </summary>
    public void ShootTarget(Transform target, float facingThresholdDegrees = 10f)
    {
        if (!motorActuator || !toolbeltActuator || !target)
            return;

        toolbeltActuator.SetActiveStance(true);
        motorActuator.AimAt(target.position);

        if (IsFacingTarget(target, facingThresholdDegrees))
            ActivateEquippedItem();
    }

    /// <summary>
    /// Equip a specific slot by index (1-based).
    /// </summary>
    public void EquipItem(int slotIndex)
    {
        if (!toolbeltActuator)
            return;

        toolbeltActuator.ChangeEquippedItem(Mathf.Max(1, slotIndex));
    }

    /// <summary>
    /// Aim at an interactable item and use the equipped tool to pick it up.
    /// </summary>
    public void PickUpItem(Transform interactable)
    {
        if (!toolbeltActuator)
            return;

        if (interactable && motorActuator)
            motorActuator.AimAt(interactable.position);
    }

    /// <summary>
    /// Trigger the equipped item if ready.
    /// </summary>
    public void ActivateEquippedItem()
    {
        if (!toolbeltActuator)
            return;

        toolbeltActuator.ActivateEquippedItem();
    }

    /// <summary>
    /// Check if the motor is facing the target within a threshold.
    /// </summary>
    public bool IsFacingTarget(Transform target, float thresholdDegrees)
    {
        if (!motorActuator || !target)
            return false;

        Vector3? facingDirection = FacingDirectionResolver.ResolveFacingDirection(motorActuator.Motor, motorActuator.transform);

        if (!facingDirection.HasValue)
            return false;

        Vector3 toTarget = target.position - motorActuator.transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude <= 0.0001f)
            return true;

        Vector3 planarFacing = facingDirection.Value;
        planarFacing.y = 0f;
        if (planarFacing.sqrMagnitude <= 0.0001f)
            return false;

        float angle = Vector3.Angle(planarFacing, toTarget);
        return angle <= thresholdDegrees;
    }
}
