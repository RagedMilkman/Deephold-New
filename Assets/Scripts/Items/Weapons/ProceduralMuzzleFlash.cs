using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class ProceduralMuzzleFlash : MonoBehaviour
{
    [Header("Shape")]
    [SerializeField, Min(1)] int minRayCount = 6;
    [SerializeField, Min(1)] int maxRayCount = 10;
    [SerializeField, Range(0f, 90f)] float coneAngle = 25f;
    [SerializeField] Vector2 lengthRange = new Vector2(0.2f, 0.45f);
    [SerializeField] Vector2 widthRange = new Vector2(0.008f, 0.02f);
    [SerializeField] Color flashColor = new Color(1f, 0.83f, 0.43f, 1f);

    [Header("Timing")]
    [SerializeField, Min(0.001f)] float visibleDuration = 0.05f;
    [SerializeField, Min(0.001f)] float fadeDuration = 0.05f;

    [Header("Light (optional)")]
    [SerializeField] Light flashLight;
    [SerializeField, Min(0f)] float lightIntensity = 2.5f;
    [SerializeField, Min(0f)] float lightRange = 6f;

    [SerializeField] LineRenderer lineRenderer;
    Coroutine flashRoutine;
    Gradient baseGradient;

    void Awake()
    {
        if (!ResolveLineRenderer())
            return;

        CacheGradient();
        InitializeLight();
        ConfigureLineRenderer();
        Hide();
    }

    void OnEnable()
    {
        Hide();
    }

    void OnDisable()
    {
        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }

        Hide();
    }

    void OnValidate()
    {
        minRayCount = Mathf.Max(1, minRayCount);
        maxRayCount = Mathf.Max(minRayCount, maxRayCount);
        lengthRange = new Vector2(Mathf.Max(0f, lengthRange.x), Mathf.Max(lengthRange.x, lengthRange.y));
        widthRange = new Vector2(Mathf.Max(0f, widthRange.x), Mathf.Max(widthRange.x, widthRange.y));

        if (!ResolveLineRenderer())
            return;

        CacheGradient();
        InitializeLight();
        ConfigureLineRenderer();
    }

    void ConfigureLineRenderer()
    {
        if (!ResolveLineRenderer())
            return;

        // Keep ray endpoints in the flash's local space so we can move/rotate the effect with the muzzle.
        lineRenderer.useWorldSpace = false;
        ApplyBaseGradient();
    }

    void CacheGradient()
    {
        if (!ResolveLineRenderer())
            return;

        baseGradient = new Gradient
        {
            colorKeys = new[]
            {
                new GradientColorKey(flashColor, 0f),
                new GradientColorKey(flashColor, 1f)
            },
            alphaKeys = new[]
            {
                new GradientAlphaKey(flashColor.a, 0f),
                new GradientAlphaKey(flashColor.a, 1f)
            }
        };
    }

    void ApplyBaseGradient()
    {
        if (!lineRenderer)
            return;

        lineRenderer.colorGradient = baseGradient;

        // Some line renderer shaders also read _Color/_EmissionColor; update them so the flash tint matches.
      //  if (lineRenderer.material)
      //  {
      //      if (lineRenderer.material.HasProperty("_Color"))
      //          lineRenderer.material.color = flashColor;
      //      if (lineRenderer.material.HasProperty("_EmissionColor"))
      //          lineRenderer.material.SetColor("_EmissionColor", flashColor);
      //  }
    }

    void InitializeLight()
    {
        if (!flashLight)
            flashLight = GetComponentInChildren<Light>(true);

        if (flashLight)
        {
            flashLight.intensity = 0f;
            flashLight.range = lightRange;
            flashLight.enabled = false;
        }
    }

    public void Play()
    {
        if (!ResolveLineRenderer())
            return;

        if (flashRoutine != null)
            StopCoroutine(flashRoutine);

        flashRoutine = StartCoroutine(FlashRoutine());
    }

    public void StopImmediate()
    {
        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }

        Hide();
    }

    IEnumerator FlashRoutine()
    {
        GenerateRays();
        Show();

        float elapsed = 0f;
        float totalDuration = Mathf.Max(0.0001f, visibleDuration + fadeDuration);
        float fadeStart = Mathf.Max(0f, visibleDuration);

        while (elapsed < totalDuration)
        {
            elapsed += Time.deltaTime;
            float alpha;

            if (elapsed <= fadeStart)
            {
                alpha = 1f;
            }
            else
            {
                float fadeT = Mathf.Clamp01((elapsed - fadeStart) / Mathf.Max(0.0001f, fadeDuration));
                alpha = 1f - fadeT;
            }

            ApplyAlpha(alpha);
            UpdateLight(alpha);
            yield return null;
        }

        Hide();
        flashRoutine = null;
    }

    void GenerateRays()
    {
        int rayCount = Random.Range(minRayCount, maxRayCount + 1);
        float width = Random.Range(widthRange.x, widthRange.y);
        float maxOffset = Mathf.Tan(coneAngle * Mathf.Deg2Rad);

        lineRenderer.positionCount = rayCount * 2;
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
        ApplyBaseGradient();

        for (int i = 0; i < rayCount; i++)
        {
            int startIndex = i * 2;
            int endIndex = startIndex + 1;
            Vector2 offset = Random.insideUnitCircle * maxOffset;
            Vector3 direction = new Vector3(offset.x, offset.y, 1f).normalized;
            float length = Random.Range(lengthRange.x, lengthRange.y);

            lineRenderer.SetPosition(startIndex, Vector3.zero);
            lineRenderer.SetPosition(endIndex, direction * length);
        }
    }

    void ApplyAlpha(float alpha)
    {
        if (lineRenderer == null)
            return;

        alpha = Mathf.Clamp01(alpha);
        var grad = lineRenderer.colorGradient;
        var alphas = grad.alphaKeys;
        for (int i = 0; i < alphas.Length; i++)
            alphas[i].alpha = alpha * flashColor.a;

        grad.alphaKeys = alphas;
        lineRenderer.colorGradient = grad;
    }

    void UpdateLight(float alpha)
    {
        if (!flashLight)
            return;

        flashLight.enabled = alpha > 0f;
        flashLight.intensity = lightIntensity * alpha;
        flashLight.range = lightRange;
    }

    void Show()
    {
        if (lineRenderer)
            lineRenderer.enabled = true;
        if (flashLight)
            flashLight.enabled = true;
    }

    void Hide()
    {
        if (lineRenderer)
            lineRenderer.enabled = false;
        if (flashLight)
        {
            flashLight.intensity = 0f;
            flashLight.enabled = false;
        }
    }

    bool ResolveLineRenderer()
    {
        if (lineRenderer)
            return true;

        lineRenderer = GetComponent<LineRenderer>();
        if (!lineRenderer)
            lineRenderer = GetComponentInChildren<LineRenderer>(true);

        return lineRenderer;
    }
}
