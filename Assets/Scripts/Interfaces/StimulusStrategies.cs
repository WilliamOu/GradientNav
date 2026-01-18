using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

public interface IStimulusMap
{
    float Evaluate(Vector2 playerPos);
    Vector2 GetPrimaryTarget();
}

[Serializable]
public struct PeakSpec
{
    public Vector2 Position;
    [Range(0f, 1f)] public float Amplitude;

    public PeakSpec(Vector2 position, float amplitude)
    {
        Position = position;
        Amplitude = amplitude;
    }
}

public class GaussianMap : IStimulusMap
{
    private Vector2 center;
    private float sigma;

    public GaussianMap(Vector2 center, float mapRadius, float sigmaScale)
    {
        this.center = center;
        sigma = mapRadius / sigmaScale;
    }

    public float Evaluate(Vector2 pos)
    {
        float dist = Vector2.Distance(pos, center);
        return Mathf.Exp(-(dist * dist) / (2f * sigma * sigma));
    }

    public Vector2 GetPrimaryTarget() => center;
}

public class LinearMap : IStimulusMap
{
    private Vector2 center;
    private float falloffRadius;

    public LinearMap(Vector2 center, float mapRadius, float sigmaScale)
    {
        this.center = center;
        // If scale is 1.0, it hits 0 intensity at the wall.
        // If scale is 2.0, it hits 0 intensity halfway to the wall.
        falloffRadius = mapRadius / sigmaScale;
    }

    public float Evaluate(Vector2 pos)
    {
        float dist = Vector2.Distance(pos, center);
        // Simple linear interpolation
        return Mathf.Clamp01(1f - (dist / falloffRadius));
    }

    public Vector2 GetPrimaryTarget() => center;
}

public class InverseMap : IStimulusMap
{
    private Vector2 center;
    private float sigma;

    public InverseMap(Vector2 center, float mapRadius, float sigmaScale)
    {
        this.center = center;
        sigma = mapRadius / sigmaScale;
    }

    public float Evaluate(Vector2 pos)
    {
        float dist = Vector2.Distance(pos, center);
        // 1.0 minus the Gaussian creates the "Valley"
        return 1f - Mathf.Exp(-(dist * dist) / (2f * sigma * sigma));
    }

    // For inverse, the "Target" is technically the walls, but we store center for reference
    public Vector2 GetPrimaryTarget() => center;
}

public class MultiPeakMap : IStimulusMap
{
    private readonly List<PeakSpec> peaks;
    private readonly float sigma;
    private readonly Vector2 brightestPeakPos;

    public MultiPeakMap(float mapRadius, float sigmaScale, IReadOnlyList<PeakSpec> peakSpecs)
    {
        sigma = (mapRadius / sigmaScale) * 0.8f;

        if (peakSpecs == null || peakSpecs.Count == 0)
        {
            // Safe fallback: single brightest peak at origin
            peaks = new List<PeakSpec> { new PeakSpec(Vector2.zero, 1f) };
        }
        else
        {
            peaks = peakSpecs.ToList();
        }

        // Define "primary target" deterministically: highest amplitude.
        // Tie-break: first occurrence.
        float bestAmp = float.NegativeInfinity;
        Vector2 bestPos = peaks[0].Position;

        for (int i = 0; i < peaks.Count; i++)
        {
            if (peaks[i].Amplitude > bestAmp)
            {
                bestAmp = peaks[i].Amplitude;
                bestPos = peaks[i].Position;
            }
        }

        brightestPeakPos = bestPos;
    }

    public float Evaluate(Vector2 pos)
    {
        float maxIntensity = 0f;

        for (int i = 0; i < peaks.Count; i++)
        {
            var peak = peaks[i];
            float dist = Vector2.Distance(pos, peak.Position);
            float gauss = Mathf.Exp(-(dist * dist) / (2f * sigma * sigma));
            float finalVal = gauss * Mathf.Clamp01(peak.Amplitude);

            if (finalVal > maxIntensity) maxIntensity = finalVal;
        }

        return maxIntensity;
    }

    public Vector2 GetPrimaryTarget() => brightestPeakPos;
}

public class TorusMap : IStimulusMap
{
    private Vector2 center;
    private float ringRadius;
    private float sigma;

    public TorusMap(Vector2 center, float mapRadius, float sigmaScale)
    {
        this.center = center;

        // Ring sits at 50% of the map radius
        ringRadius = mapRadius * 0.5f;

        // Sharper than a standard Gaussian because it's a thin ring
        sigma = (mapRadius / sigmaScale) * 0.5f;
    }

    public float Evaluate(Vector2 pos)
    {
        float distFromCenter = Vector2.Distance(pos, center);

        // The "Input Distance" is how far we are from the RING (absolute difference)
        float distFromRing = Mathf.Abs(distFromCenter - ringRadius);

        return Mathf.Exp(-(distFromRing * distFromRing) / (2f * sigma * sigma));
    }

    // Returns the center of the ring.
    // NOTE: In data analysis, remember that for Type "Torus", the goal is a ring AROUND this point.
    public Vector2 GetPrimaryTarget() => center;
}