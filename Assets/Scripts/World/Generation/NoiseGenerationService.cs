using UnityEngine;

[RequireComponent(typeof(NoiseGenerator))]
[DefaultExecutionOrder(ServiceExecutionOrder.NoiseGeneration)]
public class NoiseGenerationService : MonoBehaviour
{
    [SerializeField] NoiseGenerator noiseGenerator;

    [Header("Movement Weight")] 
    [SerializeField, Min(1)] int minMovementWeight = 6;
    [SerializeField, Min(1)] int maxMovementWeight = 14;

    void Awake() => ResolveNoiseGenerator();

#if UNITY_EDITOR
    void OnValidate() => ResolveNoiseGenerator();
#endif

    public float[,] RequestNoiseMap(int width, int height)
    {
        ResolveNoiseGenerator();

        int safeWidth = Mathf.Max(0, width);
        int safeHeight = Mathf.Max(0, height);

        if (safeWidth == 0 || safeHeight == 0)
            return new float[safeWidth, safeHeight];

        if (!noiseGenerator)
        {
            Debug.LogWarning("NoiseGenerationService requires a NoiseGenerator component.");
            return new float[safeWidth, safeHeight];
        }

        var noiseMap = noiseGenerator.GenerateNoiseMap(safeWidth, safeHeight);
        return noiseMap ?? new float[safeWidth, safeHeight];
    }

    public void ApplyNoiseMap(GridDirector gridDirector)
    {
        if (!gridDirector || !gridDirector.isServer)
            return;

        int width = gridDirector.Width;
        int height = gridDirector.Height;
        if (width <= 0 || height <= 0)
            return;

        var requestedNoise = RequestNoiseMap(width, height);
        if (requestedNoise == null || requestedNoise.GetLength(0) != width || requestedNoise.GetLength(1) != height)
            return;

        int minWeight = minMovementWeight;
        int maxWeight = maxMovementWeight;
        if (minWeight > maxWeight)
        {
            var temp = minWeight;
            minWeight = maxWeight;
            maxWeight = temp;
        }

        var weights = new byte[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float value = requestedNoise[x, y];
                float interpolated = Mathf.Lerp(minWeight, maxWeight, value);
                weights[x, y] = (byte)Mathf.Clamp(Mathf.RoundToInt(interpolated), minWeight, maxWeight);
            }
        }

        gridDirector.ServerApplyMovementWeights(weights);
    }

    void ResolveNoiseGenerator()
    {
        if (!noiseGenerator)
        {
            noiseGenerator = GetComponent<NoiseGenerator>();
        }
    }
}
