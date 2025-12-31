using UnityEngine;

public static class FacingDirectionResolver
{
    public static Vector3? ResolveFacingDirection(TopDownMotor motor, Transform fallback)
    {
        if (motor)
        {
            var origin = (Vector3?)motor.FacingOrigin;
            var headLookTarget = motor.CurrentHeadLookTarget;
            if (headLookTarget.HasValue && origin.HasValue)
            {
                var direction = headLookTarget.Value - origin.Value;
                if (direction.sqrMagnitude > 0.0001f)
                    return direction;
            }

            if (motor.HasCursorTarget && origin.HasValue)
            {
                var target = motor.PlayerTarget;
                if (motor.PlayerTargetIsFloor)
                {
                    target += Vector3.up * 1.5f;
                }

                var direction = target - origin.Value;
                if (direction.sqrMagnitude > 0.0001f)
                    return direction;
            }

            return motor.FacingForward;
        }

        return fallback ? (Vector3?)fallback.forward : null;
    }
}
