using System.Collections.Generic;
using UnityEngine;

public static class MultiPeakSpecFactory
{
    public static List<PeakSpec> Create(int seed, float mapRadius, int count)
    {
        var rng = new System.Random(seed);
        var peaks = new List<PeakSpec>(count);

        // One guaranteed brightest peak
        peaks.Add(new PeakSpec(RandomInsideDisk(rng, mapRadius * 0.8f), 1f));

        for (int i = 1; i < count; i++)
        {
            float amp = Lerp(0.5f, 0.9f, (float)rng.NextDouble());
            peaks.Add(new PeakSpec(RandomInsideDisk(rng, mapRadius * 0.8f), amp));
        }

        return peaks;
    }

    private static Vector2 RandomInsideDisk(System.Random rng, float radius)
    {
        // Uniform disk: r = sqrt(u), theta = 2pi v
        float u = (float)rng.NextDouble();
        float v = (float)rng.NextDouble();
        float r = Mathf.Sqrt(u) * radius;
        float theta = 2f * Mathf.PI * v;
        return new Vector2(r * Mathf.Cos(theta), r * Mathf.Sin(theta));
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}

public class StimulusManager : MonoBehaviour
{
    private Vector2? activeGoalOverride;
    public static readonly List<string> MapTypes = new List<string> { "Gaussian", "Linear", "Inverse", "Multi-Peak", "Torus" };
    private IStimulusMap currentMap;

    public void GenerateMap(
    int typeIndex,
    float mapWidth,
    float mapLength,
    Vector2 centerOffset,
    Vector2? goalOverride = null,
    IReadOnlyList<PeakSpec> multiPeakSpecs = null
)
    {
        activeGoalOverride = goalOverride;
        float mapRadius = Mathf.Min(mapWidth, mapLength) / 2f;
        float sigmaScale = AppManager.Instance.Settings.SigmaScale;

        switch (typeIndex)
        {
            case 0:
                currentMap = new GaussianMap(centerOffset, mapRadius, sigmaScale);
                break;
            case 1:
                currentMap = new LinearMap(centerOffset, mapRadius, sigmaScale);
                break;
            case 2:
                currentMap = new InverseMap(centerOffset, mapRadius, sigmaScale);
                break;
            case 3:
                currentMap = new MultiPeakMap(mapRadius, sigmaScale, multiPeakSpecs);
                break;
            case 4:
                currentMap = new TorusMap(centerOffset, mapRadius, sigmaScale);
                break;
            default:
                currentMap = new GaussianMap(centerOffset, mapRadius, sigmaScale);
                break;
        }

        AppManager.Instance.Session.GoalPosition = goalOverride ?? currentMap.GetPrimaryTarget();
    }

    public float GetIntensity(Vector3 worldPos)
    {
        if (currentMap == null) return 0f;
        Vector2 pos2D = new Vector2(worldPos.x, worldPos.z);
        return Mathf.Clamp01(currentMap.Evaluate(pos2D));
    }

    public Vector2 GetTargetPosition()
    {
        return activeGoalOverride ?? (currentMap != null ? currentMap.GetPrimaryTarget() : Vector2.zero);
    }
}
