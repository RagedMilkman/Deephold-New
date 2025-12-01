using UnityEngine;

public abstract class NoiseGenerator : MonoBehaviour
{
    public abstract float[,] GenerateNoiseMap(int width, int height);
}
