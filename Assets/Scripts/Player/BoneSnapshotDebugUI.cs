using UnityEngine;

/// <summary>
/// Optional on-screen readout to help determine whether snapshot issues come
/// from sending or applying.
/// </summary>
public class BoneSnapshotDebugUI : MonoBehaviour
{
    [SerializeField] private BoneSnapshotReplicator _replicator;
    [SerializeField] private GhostFollower _ghostFollower;
    [SerializeField] private Vector2 _screenPosition = new(20f, 20f);
    [SerializeField] private bool _showBackground = true;

    private GUIStyle _labelStyle;

    private void Awake()
    {
        if (!_replicator)
            _replicator = GetComponentInChildren<BoneSnapshotReplicator>();
        if (!_ghostFollower)
            _ghostFollower = GetComponentInChildren<GhostFollower>();

        _labelStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            normal = { textColor = Color.white },
            fontSize = 12
        };
    }

    private void OnGUI()
    {
        if (_replicator == null && _ghostFollower == null)
            return;

        string text = BuildText();
        Vector2 size = _labelStyle.CalcSize(new GUIContent(text));
        Rect rect = new Rect(_screenPosition, size + new Vector2(8f, 8f));

        if (_showBackground)
            GUI.Box(rect, GUIContent.none);

        GUI.Label(rect, text, _labelStyle);
    }

    private string BuildText()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        if (_replicator != null)
        {
            sb.AppendLine("Sender (BoneSnapshotReplicator)");
            sb.AppendLine($"  Sent: {_replicator.SentSnapshots}");
            sb.AppendLine($"  Last send time: {_replicator.LastSendTime:F3}s");
            sb.AppendLine($"  Received: {_replicator.ReceivedSnapshots}");
            sb.AppendLine($"  Last receive time: {_replicator.LastReceiveTime:F3}s");
            sb.AppendLine();
        }

        if (_ghostFollower != null)
        {
            sb.AppendLine("Receiver (GhostFollower)");
            sb.AppendLine($"  Enqueued: {_ghostFollower.EnqueuedSnapshots}");
            sb.AppendLine($"  Last enqueue time: {_ghostFollower.LastEnqueueTime:F3}s");
            sb.AppendLine($"  Buffered: {_ghostFollower.BufferedSnapshots}");
            sb.AppendLine($"  Applied: {_ghostFollower.AppliedSnapshots}");
            sb.AppendLine($"  Last apply time: {_ghostFollower.LastApplyTime:F3}s");
        }

        return sb.ToString();
    }
}
