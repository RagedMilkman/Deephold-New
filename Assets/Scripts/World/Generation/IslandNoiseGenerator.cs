using System;
using UnityEngine;

[DisallowMultipleComponent]
public class IslandNoiseGenerator : NoiseGenerator
{
    [Header("Island Layout")]
    [SerializeField, Tooltip("Distance in cells between island centers.")]
    [Min(1f)] float islandSpacing = 32f;
    [SerializeField, Tooltip("Radius in cells for each island's high point.")]
    [Min(0.5f)] float islandRadius = 24f;
    [SerializeField, Tooltip("Higher values create taller, sharper peaks. Lower values make gentler slopes.")]
    [Min(0.1f)] float islandSteepness = 2f;
    [SerializeField, Tooltip("Randomly jitters island centers to avoid a perfect grid.")]
    [Range(0f, 1f)] float islandJitter = 0.35f;
    [SerializeField, Tooltip("Offset applied to the sampling domain in cells.")]
    Vector2 domainOffset = Vector2.zero;

    [Header("Randomization")]
    [SerializeField] bool useFixedSeed = true;
    [SerializeField] int seed = 0;

    [Header("Profile")]
    [SerializeField, Tooltip("Controls the falloff from the island center (X = normalized distance, Y = height multiplier).")]
    AnimationCurve islandProfile = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    void Awake()
    {
        EnsureProfileDefaults();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        EnsureProfileDefaults();
    }
#endif

    public override float[,] GenerateNoiseMap(int width, int height)
    {
        int safeWidth = Mathf.Max(0, width);
        int safeHeight = Mathf.Max(0, height);
        var noiseMap = new float[safeWidth, safeHeight];
        if (safeWidth == 0 || safeHeight == 0)
            return noiseMap;

        float spacing = Mathf.Max(0.001f, islandSpacing);
        float radius = Mathf.Max(0.001f, islandRadius);
        float jitter = Mathf.Clamp01(islandJitter);
        EnsureProfileDefaults();
        var profile = islandProfile;
        int resolvedSeed = ResolveSeed();

        int neighborRange = Mathf.Max(1, Mathf.CeilToInt(radius / spacing));

        for (int y = 0; y < safeHeight; y++)
        {
            float sampleY = y + domainOffset.y;
            int baseCellY = Mathf.FloorToInt(sampleY / spacing);

            for (int x = 0; x < safeWidth; x++)
            {
                float sampleX = x + domainOffset.x;
                int baseCellX = Mathf.FloorToInt(sampleX / spacing);
                float maxContribution = 0f;
                Vector2 samplePoint = new Vector2(sampleX, sampleY);

                for (int dy = -neighborRange; dy <= neighborRange; dy++)
                {
                    int cellY = baseCellY + dy;
                    for (int dx = -neighborRange; dx <= neighborRange; dx++)
                    {
                        int cellX = baseCellX + dx;
                        Vector2 center = GetIslandCenter(cellX, cellY, spacing, jitter, resolvedSeed);
                        float distance = Vector2.Distance(samplePoint, center);
                        float normalizedDistance = distance / radius;
                        if (normalizedDistance >= 1f)
                            continue;

                        float baseValue = Mathf.Clamp01(1f - normalizedDistance);
                        if (!Mathf.Approximately(islandSteepness, 1f))
                            baseValue = Mathf.Pow(baseValue, islandSteepness);

                        float profileMultiplier = Mathf.Clamp01(profile.Evaluate(Mathf.Clamp01(normalizedDistance)));
                        float contribution = baseValue * profileMultiplier;
                        if (contribution > maxContribution)
                            maxContribution = contribution;
                    }
                }

                noiseMap[x, y] = Mathf.Clamp01(maxContribution);
            }
        }

        return noiseMap;
    }

    int ResolveSeed()
    {
        if (useFixedSeed)
            return seed;

        return unchecked(Environment.TickCount ^ GetInstanceID() ^ GetHashCode());
    }

    Vector2 GetIslandCenter(int cellX, int cellY, float spacing, float jitter, int baseSeed)
    {
        float baseX = (cellX + 0.5f) * spacing;
        float baseY = (cellY + 0.5f) * spacing;

        if (jitter <= 0f)
            return new Vector2(baseX, baseY);

        System.Random random = CreateDeterministicRandom(cellX, cellY, baseSeed);
        float jitterRange = spacing * 0.5f * jitter;
        float offsetX = ((float)random.NextDouble() * 2f - 1f) * jitterRange;
        float offsetY = ((float)random.NextDouble() * 2f - 1f) * jitterRange;
        return new Vector2(baseX + offsetX, baseY + offsetY);
    }

    System.Random CreateDeterministicRandom(int cellX, int cellY, int baseSeed)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + baseSeed;
            hash = hash * 31 + cellX;
            hash = hash * 31 + cellY;
            hash ^= hash << 13;
            hash ^= hash >> 17;
            hash ^= hash << 5;
            hash = Mathf.Abs(hash);
            if (hash == 0)
                hash = 1;
            return new System.Random(hash);
        }
    }

    void EnsureProfileDefaults()
    {
        if (islandProfile == null || islandProfile.length == 0)
        {
            islandProfile = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        }
    }
}
