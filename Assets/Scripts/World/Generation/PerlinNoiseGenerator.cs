using System;
using UnityEngine;

[DisallowMultipleComponent]
public class PerlinNoiseGenerator : NoiseGenerator
{
    [Header("Noise Settings")]
    [SerializeField, Min(0.0001f)] float scale = 20f;
    [SerializeField, Range(1, 8)] int octaves = 4;
    [SerializeField, Min(0f)] float persistence = 0.5f;
    [SerializeField, Min(1f)] float lacunarity = 2f;
    [SerializeField] int seed = 0;
    [SerializeField] Vector2 offset = Vector2.zero;
    [SerializeField, Tooltip("Adjusts the weight of each octave. X is the normalized octave index, Y is the amplitude multiplier.")]
    AnimationCurve octaveWeights = AnimationCurve.Linear(0f, 1f, 1f, 1f);
    [SerializeField, Tooltip("Boosts or flattens contrast after normalization. Values > 1 emphasize high values, < 1 flatten them."), Min(0.01f)]
    float redistributionPower = 1f;
    [SerializeField, Tooltip("Optional curve applied after normalization for fine control over the noise distribution.")]
    AnimationCurve redistributionCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [SerializeField, Tooltip("Clamp the redistributed values to the 0-1 range after applying the curve.")]
    bool clampRedistribution = true;

    public override float[,] GenerateNoiseMap(int width, int height)
    {
        int safeWidth = Mathf.Max(0, width);
        int safeHeight = Mathf.Max(0, height);
        var noiseMap = new float[safeWidth, safeHeight];
        if (safeWidth == 0 || safeHeight == 0)
            return noiseMap;

        float validatedScale = Mathf.Max(0.0001f, scale);
        var random = new System.Random(seed);
        var octaveOffsets = new Vector2[octaves];
        for (int i = 0; i < octaves; i++)
        {
            float offsetX = random.Next(-100000, 100000) + offset.x;
            float offsetY = random.Next(-100000, 100000) + offset.y;
            octaveOffsets[i] = new Vector2(offsetX, offsetY);
        }

        float maxNoiseHeight = float.MinValue;
        float minNoiseHeight = float.MaxValue;

        var octaveCurve = octaveWeights ?? AnimationCurve.Linear(0f, 1f, 1f, 1f);
        var redistribution = redistributionCurve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);

        float halfWidth = safeWidth / 2f;
        float halfHeight = safeHeight / 2f;

        for (int y = 0; y < safeHeight; y++)
        {
            for (int x = 0; x < safeWidth; x++)
            {
                float amplitude = 1f;
                float frequency = 1f;
                float noiseHeight = 0f;

                for (int i = 0; i < octaves; i++)
                {
                    float sampleX = (x - halfWidth) / validatedScale * frequency + octaveOffsets[i].x;
                    float sampleY = (y - halfHeight) / validatedScale * frequency + octaveOffsets[i].y;

                    float perlinValue = Mathf.PerlinNoise(sampleX, sampleY) * 2f - 1f;
                    float weight = octaveCurve.Evaluate(octaves <= 1 ? 0f : (float)i / (octaves - 1f));
                    noiseHeight += perlinValue * amplitude * weight;

                    amplitude *= persistence;
                    frequency *= lacunarity;
                }

                if (noiseHeight > maxNoiseHeight) maxNoiseHeight = noiseHeight;
                if (noiseHeight < minNoiseHeight) minNoiseHeight = noiseHeight;
                noiseMap[x, y] = noiseHeight;
            }
        }

        for (int y = 0; y < safeHeight; y++)
        {
            for (int x = 0; x < safeWidth; x++)
            {
                float normalized = Mathf.InverseLerp(minNoiseHeight, maxNoiseHeight, noiseMap[x, y]);
                if (!Mathf.Approximately(redistributionPower, 1f))
                    normalized = Mathf.Pow(normalized, redistributionPower);

                normalized = redistribution.Evaluate(normalized);

                if (clampRedistribution)
                    normalized = Mathf.Clamp01(normalized);

                noiseMap[x, y] = normalized;
            }
        }

        return noiseMap;
    }
}
