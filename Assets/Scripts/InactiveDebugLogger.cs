using System.Diagnostics;
using UnityEngine;

/// <summary>
/// Utility component that reports the call stack whenever its GameObject or component
/// is deactivated. Attach to any object you want to monitor.
/// </summary>
[DisallowMultipleComponent]
public class InactiveDebugLogger : MonoBehaviour
{
    private void OnDisable()
    {
        // Skip the current frame to remove OnDisable from the stack trace for clarity.
        var stackTrace = new StackTrace(skipFrames: 1, fNeedFileInfo: true);
        var header = $"[InactiveDebugLogger] '{name}' on path '{transform.GetHierarchyPath()}' became inactive.";
        UnityEngine.Debug.Log($"{header}\nCall Stack:\n{stackTrace}", this);
    }
}

public static class TransformExtensions
{
    /// <summary>
    /// Builds a human-readable path for the transform within the scene hierarchy.
    /// </summary>
    public static string GetHierarchyPath(this Transform transform)
    {
        return transform.parent == null
            ? transform.name
            : $"{transform.parent.GetHierarchyPath()}/{transform.name}";
    }
}
