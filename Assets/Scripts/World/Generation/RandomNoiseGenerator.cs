using System;
using UnityEngine;

[DisallowMultipleComponent]
public class RandomNoiseGenerator : NoiseGenerator
{
    [Header("Random Noise Settings")]
    [SerializeField] bool useFixedSeed = false;
    [SerializeField] int seed = 0;
    [SerializeField, Tooltip("Push values toward 0 and 1 (>0) or toward 0.5 (<0).")]
    float extremeBias = 0f;
    [SerializeField, Tooltip("Optional curve to remap biased samples; X is the raw sample, Y is the result.")]
    AnimationCurve distributionCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    System.Random cachedRandom;

    void Awake()
    {
        PrepareRandom();
        EnsureCurveDefaults();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        PrepareRandom();
        EnsureCurveDefaults();
    }
#endif

    public override float[,] GenerateNoiseMap(int width, int height)
    {
        int safeWidth = Mathf.Max(0, width);
        int safeHeight = Mathf.Max(0, height);
        var noiseMap = new float[safeWidth, safeHeight];
        if (safeWidth == 0 || safeHeight == 0)
            return noiseMap;

        var random = ResolveRandom();

        for (int y = 0; y < safeHeight; y++)
        {
            for (int x = 0; x < safeWidth; x++)
            {
                noiseMap[x, y] = SampleDistribution(random);
            }
        }

        if (!useFixedSeed)
        {
            cachedRandom = null;
        }

        return noiseMap;
    }

    float SampleDistribution(System.Random random)
    {
        float sample = Mathf.Clamp01((float)random.NextDouble());
        sample = ApplyExtremeBias(sample);
        sample = ApplyDistributionCurve(sample);
        return sample;
    }

    float ApplyExtremeBias(float sample)
    {
        if (Mathf.Approximately(extremeBias, 0f))
            return sample;

        float clamped = Mathf.Clamp01(sample);
        float distance = Mathf.Abs(clamped - 0.5f);
        float normalizedDistance = Mathf.Clamp01(distance * 2f);
        if (normalizedDistance <= 0f)
            return 0.5f;

        float exponent = extremeBias > 0f
            ? 1f / (1f + extremeBias)
            : 1f - extremeBias;

        float remappedDistance = Mathf.Pow(normalizedDistance, exponent);
        float sign = clamped < 0.5f ? -1f : 1f;
        float result = 0.5f + sign * remappedDistance * 0.5f;
        return Mathf.Clamp01(result);
    }

    float ApplyDistributionCurve(float sample)
    {
        if (!HasCurveData(distributionCurve))
            return sample;

        return Mathf.Clamp01(distributionCurve.Evaluate(sample));
    }

    bool HasCurveData(AnimationCurve curve)
    {
        return curve != null && curve.length > 0;
    }

    void EnsureCurveDefaults()
    {
        if (distributionCurve == null || distributionCurve.length == 0)
        {
            distributionCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }
    }

    System.Random ResolveRandom()
    {
        if (cachedRandom == null)
        {
            cachedRandom = useFixedSeed
                ? new System.Random(seed)
                : new System.Random(unchecked(Environment.TickCount ^ GetInstanceID() ^ GetHashCode()));
        }

        return cachedRandom;
    }

    void PrepareRandom()
    {
        cachedRandom = useFixedSeed ? new System.Random(seed) : null;
    }
}
