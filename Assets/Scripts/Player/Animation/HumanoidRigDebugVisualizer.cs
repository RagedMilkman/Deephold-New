using UnityEngine;

internal static class HumanoidRigDebugVisualizer
{
    internal static void DrawComfortRange(
        HumanoidRigAnimator.BonePose pose,
        Vector3 forward,
        Vector3 up,
        Vector3 right,
        float yawLimit,
        float pitchLimit,
        float length,
        Color color)
    {
        if (pose.Transform == null || length <= 0f)
        {
            return;
        }

        var parent = pose.Transform.parent;
        if (parent == null)
        {
            return;
        }

        Vector3 worldForward = parent.TransformDirection(forward);
        Vector3 worldUp = parent.TransformDirection(up);
        Vector3 worldRight = parent.TransformDirection(right);

        if (worldForward.sqrMagnitude < 0.0001f || worldUp.sqrMagnitude < 0.0001f || worldRight.sqrMagnitude < 0.0001f)
        {
            return;
        }

        worldForward.Normalize();
        worldUp.Normalize();
        worldRight.Normalize();

        Vector3 origin = pose.Transform.position;

        Vector3 yawPositive = ComputeYawPitchDirection(worldForward, worldUp, worldRight, yawLimit, 0f);
        Vector3 yawNegative = ComputeYawPitchDirection(worldForward, worldUp, worldRight, -yawLimit, 0f);
        Vector3 pitchPositive = ComputeYawPitchDirection(worldForward, worldUp, worldRight, 0f, pitchLimit);
        Vector3 pitchNegative = ComputeYawPitchDirection(worldForward, worldUp, worldRight, 0f, -pitchLimit);

        Vector3 cornerPP = ComputeYawPitchDirection(worldForward, worldUp, worldRight, yawLimit, pitchLimit);
        Vector3 cornerPN = ComputeYawPitchDirection(worldForward, worldUp, worldRight, yawLimit, -pitchLimit);
        Vector3 cornerNP = ComputeYawPitchDirection(worldForward, worldUp, worldRight, -yawLimit, pitchLimit);
        Vector3 cornerNN = ComputeYawPitchDirection(worldForward, worldUp, worldRight, -yawLimit, -pitchLimit);

        DrawComfortSegment(origin, yawPositive, color, length);
        DrawComfortSegment(origin, yawNegative, color, length);
        DrawComfortSegment(origin, pitchPositive, color, length);
        DrawComfortSegment(origin, pitchNegative, color, length);

        DrawComfortSegment(origin, cornerPP, color, length);
        DrawComfortSegment(origin, cornerPN, color, length);
        DrawComfortSegment(origin, cornerNP, color, length);
        DrawComfortSegment(origin, cornerNN, color, length);

        DrawComfortConnection(origin, cornerPP, cornerPN, color, length);
        DrawComfortConnection(origin, cornerPN, cornerNN, color, length);
        DrawComfortConnection(origin, cornerNN, cornerNP, color, length);
        DrawComfortConnection(origin, cornerNP, cornerPP, color, length);
    }

    internal static void DrawHeadTargetLine(HumanoidRigAnimator.BonePose headPose, Vector3 targetPosition)
    {
        if (headPose.Transform == null)
        {
            return;
        }

        Debug.DrawLine(headPose.Transform.position, targetPosition, Color.cyan, Time.deltaTime, false);
    }

    internal static void DrawChestTargetLine(HumanoidRigAnimator.BonePose chestPose, Vector3 targetPosition)
    {
        if (chestPose.Transform == null)
        {
            return;
        }

        Debug.DrawLine(chestPose.Transform.position, targetPosition, Color.yellow, Time.deltaTime, false);
    }

    private static void DrawComfortSegment(Vector3 origin, Vector3 direction, Color color, float length)
    {
        Debug.DrawLine(origin, origin + direction * length, color, Time.deltaTime, false);
    }

    private static void DrawComfortConnection(Vector3 origin, Vector3 fromDir, Vector3 toDir, Color color, float length)
    {
        Vector3 fromPoint = origin + fromDir * length;
        Vector3 toPoint = origin + toDir * length;
        Debug.DrawLine(fromPoint, toPoint, color, Time.deltaTime, false);
    }

    private static Vector3 ComputeYawPitchDirection(Vector3 forward, Vector3 up, Vector3 right, float yaw, float pitch)
    {
        Quaternion yawRotation = Quaternion.AngleAxis(yaw, up);
        Quaternion pitchRotation = Quaternion.AngleAxis(pitch, right);
        return (pitchRotation * yawRotation) * forward;
    }
}
