using System.Collections;
using UnityEngine;

/// <summary>
/// Handles a simple procedural swing and return animation for melee items.
/// </summary>
[DisallowMultipleComponent]
public class ProceduralMeleeSwing : MonoBehaviour
{
    [SerializeField] private Transform swingRoot;

    [Header("Swing Offsets")]
    [SerializeField] private Vector3 swingLocalPositionOffset = new Vector3(0f, -0.04f, -0.18f);
    [SerializeField] private Vector3 swingLocalEulerOffset = new Vector3(-40f, 0f, 6f);

    [Header("Timing")]
    [SerializeField, Min(0f)] private float swingDuration = 0.14f;
    [SerializeField, Min(0f)] private float returnDuration = 0.1f;

    [Header("Curves")]
    [SerializeField] private AnimationCurve swingCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve returnCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Coroutine swingRoutine;
    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;
    private bool cachedDefaults;

    private Transform SwingRoot => swingRoot ? swingRoot : transform;

    private void Awake()
    {
        CacheDefaults();
    }

    private void OnEnable()
    {
        CacheDefaults();
        ResetSwing();
    }

    private void OnDisable()
    {
        ResetSwing();
    }

    /// <summary>
    /// Plays a swing animation from the current rest position.
    /// </summary>
    public void Play()
    {
        if (!isActiveAndEnabled)
            return;

        CacheDefaults();

        if (swingRoutine != null)
            StopCoroutine(swingRoutine);

        swingRoutine = StartCoroutine(SwingRoutine());
    }

    private void CacheDefaults()
    {
        var root = SwingRoot;
        if (!root)
            return;

        if (cachedDefaults)
            return;

        initialLocalPosition = root.localPosition;
        initialLocalRotation = root.localRotation;
        cachedDefaults = true;
    }

    private void ResetSwing()
    {
        if (!cachedDefaults)
            CacheDefaults();

        var root = SwingRoot;
        if (!root)
            return;

        if (swingRoutine != null)
        {
            StopCoroutine(swingRoutine);
            swingRoutine = null;
        }

        root.localPosition = initialLocalPosition;
        root.localRotation = initialLocalRotation;
    }

    private IEnumerator SwingRoutine()
    {
        var root = SwingRoot;
        if (!root)
            yield break;

        Vector3 startPos = initialLocalPosition;
        Quaternion startRot = initialLocalRotation;
        Vector3 swingPos = startPos + swingLocalPositionOffset;
        Quaternion swingRot = startRot * Quaternion.Euler(swingLocalEulerOffset);

        float elapsed = 0f;
        float duration = Mathf.Max(0.0001f, swingDuration);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float curved = swingCurve.Evaluate(t);
            root.localPosition = Vector3.Lerp(startPos, swingPos, curved);
            root.localRotation = Quaternion.Slerp(startRot, swingRot, curved);
            yield return null;
        }

        elapsed = 0f;
        duration = Mathf.Max(0.0001f, returnDuration);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float curved = returnCurve.Evaluate(t);
            root.localPosition = Vector3.Lerp(swingPos, startPos, curved);
            root.localRotation = Quaternion.Slerp(swingRot, startRot, curved);
            yield return null;
        }

        root.localPosition = startPos;
        root.localRotation = startRot;
        swingRoutine = null;
    }
}
