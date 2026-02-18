using UnityEngine;
using System;

public interface IRng
{
    // Inclusive min, exclusive max (matches Unity's int Range behavior)
    int Range(int minInclusive, int maxExclusive);

    // Inclusive min, inclusive max is fine too, but keep it consistent.
    float Range(float minInclusive, float maxInclusive);

    // Uniform random point inside unit circle
    Vector2 InsideUnitCircle();
}

public sealed class UnityRng : IRng
{
    public int Range(int minInclusive, int maxExclusive) =>
        UnityEngine.Random.Range(minInclusive, maxExclusive);

    public float Range(float minInclusive, float maxInclusive) =>
        UnityEngine.Random.Range(minInclusive, maxInclusive);

    public Vector2 InsideUnitCircle() =>
        UnityEngine.Random.insideUnitCircle;
}

public sealed class SeededRng : IRng
{
    private readonly System.Random rng;

    public SeededRng(int seed) => rng = new System.Random(seed);

    public int Range(int minInclusive, int maxExclusive) =>
        rng.Next(minInclusive, maxExclusive);

    public float Range(float minInclusive, float maxInclusive)
    {
        // NextDouble is [0,1)
        double t = rng.NextDouble();
        return (float)(minInclusive + (maxInclusive - minInclusive) * t);
    }

    public Vector2 InsideUnitCircle()
    {
        // Uniform disk sampling: r = sqrt(u), theta = 2pi v
        float u = (float)rng.NextDouble();
        float v = (float)rng.NextDouble();
        float r = Mathf.Sqrt(u);
        float theta = 2f * Mathf.PI * v;

        return new Vector2(r * Mathf.Cos(theta), r * Mathf.Sin(theta));
    }
}