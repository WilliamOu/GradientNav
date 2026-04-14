using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public interface IStimulusMap
{
    float Evaluate(Vector2 playerPos);
    Vector2 GetPrimaryTarget();

    float ScaleParameter { get; }
    float BrightnessMaxPercent { get; }
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
        sigma = mapRadius * sigmaScale;
    }

    public float Evaluate(Vector2 pos)
    {
        float dist = Vector2.Distance(pos, center);
        return Mathf.Exp(-(dist * dist) / (2f * sigma * sigma));
    }

    public Vector2 GetPrimaryTarget() => center;

    public float ScaleParameter => 1f;
    public float BrightnessMaxPercent => 1f;
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
        falloffRadius = mapRadius * sigmaScale;
    }

    public float Evaluate(Vector2 pos)
    {
        float dist = Vector2.Distance(pos, center);
        // Simple linear interpolation
        return Mathf.Clamp01(1f - (dist / falloffRadius));
    }

    public Vector2 GetPrimaryTarget() => center;

    public float ScaleParameter => 1f;
    public float BrightnessMaxPercent => 1f;
}

public class InverseMap : IStimulusMap
{
    private Vector2 center;
    private float sigma;

    public InverseMap(Vector2 center, float mapRadius, float sigmaScale)
    {
        this.center = center;
        sigma = mapRadius * sigmaScale;
    }

    public float Evaluate(Vector2 pos)
    {
        float dist = Vector2.Distance(pos, center);
        // 1.0 minus the Gaussian creates the "Valley"
        return 1f - Mathf.Exp(-(dist * dist) / (2f * sigma * sigma));
    }

    // For inverse, the "Target" is technically the walls, but we store center for reference
    public Vector2 GetPrimaryTarget() => center;

    public float ScaleParameter => 1f;
    public float BrightnessMaxPercent => 1f;
}

public class MultiPeakMap : IStimulusMap
{
    private readonly List<PeakSpec> peaks;
    private readonly float sigma;
    private readonly Vector2 brightestPeakPos;

    public MultiPeakMap(float mapRadius, float sigmaScale, IReadOnlyList<PeakSpec> peakSpecs)
    {
        sigma = (mapRadius * sigmaScale);

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
        float totalIntensity = 0f;

        for (int i = 0; i < peaks.Count; i++)
        {
            var peak = peaks[i];
            float dist = Vector2.Distance(pos, peak.Position);

            float gauss = Mathf.Exp(-(dist * dist) / (2f * sigma * sigma));

            totalIntensity += gauss * peak.Amplitude;
        }

        return totalIntensity;
    }

    public Vector2 GetPrimaryTarget() => brightestPeakPos;

    public float ScaleParameter => 1f;
    public float BrightnessMaxPercent => 1f;
}

public class LinearMultiPeakMap : IStimulusMap
{
    private readonly List<PeakSpec> peaks;
    private readonly float[] falloffRadii;
    private readonly Vector2 brightestPeakPos;

    public LinearMultiPeakMap(float mapRadius, IReadOnlyList<float> sigmaScales, IReadOnlyList<PeakSpec> peakSpecs)
    {
        if (peakSpecs == null || peakSpecs.Count == 0)
        {
            peaks = new List<PeakSpec> { new PeakSpec(Vector2.zero, 1f) };
        }
        else
        {
            peaks = peakSpecs.ToList();
        }

        // Determine primary target: highest amplitude, first wins ties.
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

        // Build per-peak falloff radii from sigmaScales
        falloffRadii = new float[peaks.Count];

        float defaultScale = 1f;
        bool hasSigmas = sigmaScales != null && sigmaScales.Count > 0;

        for (int i = 0; i < peaks.Count; i++)
        {
            float scale;
            if (!hasSigmas)
            {
                scale = defaultScale;
            }
            else
            {
                int idx = Mathf.Min(i, sigmaScales.Count - 1);
                scale = sigmaScales[idx];
            }

            // Prevent division by zero or negative radii.
            float radius = mapRadius * Mathf.Max(scale, 0.0001f);
            falloffRadii[i] = radius;
        }
    }

    public float Evaluate(Vector2 pos)
    {
        float totalIntensity = 0f;

        for (int i = 0; i < peaks.Count; i++)
        {
            var peak = peaks[i];
            float dist = Vector2.Distance(pos, peak.Position);

            float falloffRadius = falloffRadii[i];
            float linear = Mathf.Clamp01(1f - (dist / falloffRadius));

            totalIntensity += linear * peak.Amplitude;
        }

        return totalIntensity;
    }

    public Vector2 GetPrimaryTarget() => brightestPeakPos;

    public float ScaleParameter => 1f;
    public float BrightnessMaxPercent => 1f;
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
        sigma = (mapRadius * sigmaScale) * 0.5f;
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

    public float ScaleParameter => 1f;
    public float BrightnessMaxPercent => 1f;
}

public class MatrixMap : IStimulusMap
{
    [Serializable]
    private class MatrixMeta
    {
        public int formatVersion = 1;

        public string dataFile = "data.bin";
        public string dataEncoding = "u8_raw";

        public int width = 0;
        public int height = 0;
        public int frames = 1;

        public float scaleCm = 1f;

        // Allowed:
        // interp2D: "nearest" | "bilinear"
        // interp3D: "none" | "linear"   (none = floor frame)
        // frameMode: "clamp" | "loop"
        public string interp2D = "nearest";
        public string interp3D = "none";
        public string frameMode = "clamp";

        public float frameSeconds = 1f;

        // Optional extras
        public string outOfBounds = "zero";
        public string timeSource = "unityTime";

        public float scaleParameter = 1f;
        public float brightnessMaxPercent = 1f;
    }

    public float scaleParameter;
    public float brightnessMaxPercent;

    private readonly Vector2 center;
    private readonly string folderName;

    private readonly int width;
    private readonly int height;
    private readonly int frames;

    private readonly float cellSizeMeters;
    private readonly float frameSeconds;

    private readonly Interp2D interp2D;
    private readonly Interp3D interp3D;
    private readonly FrameMode frameMode;

    private readonly byte[] data; // flat: frame-major, then y, then x

    private bool valid = false;
    private bool clockStarted;
    private float t0;

    private enum Interp2D { Nearest, Bilinear }
    private enum Interp3D { None, Linear }
    private enum FrameMode { Clamp, Loop }

    public MatrixMap(Vector2 centerXZ, string mapFileName = null)
    {
        if (mapFileName == null) return;
        valid = true;

        center = centerXZ;
        folderName = mapFileName;

        if (string.IsNullOrWhiteSpace(folderName))
            throw new ArgumentException("MatrixMap requires a non-empty mapFileName.");

        string baseDir = Path.Combine(Application.persistentDataPath, "Matrices", folderName);
        string metaPath = Path.Combine(baseDir, "meta.json");
        if (!File.Exists(metaPath))
            throw new FileNotFoundException($"Matrix meta.json not found: {metaPath}");

        var metaJson = File.ReadAllText(metaPath);
        var meta = JsonUtility.FromJson<MatrixMeta>(metaJson);
        if (meta == null)
            throw new Exception($"Failed to parse meta.json: {metaPath}");

        // Validate / normalize meta
        if (meta.width <= 0 || meta.height <= 0)
            throw new Exception($"Invalid matrix dimensions in meta.json (width/height must be > 0). Folder: {baseDir}");
        if (meta.frames <= 0) meta.frames = 1;

        if (meta.scaleCm <= 0f)
            throw new Exception($"Invalid scaleCm in meta.json (must be > 0). Folder: {baseDir}");

        if (meta.frameSeconds <= 0f)
            throw new Exception($"Invalid frameSeconds in meta.json (must be > 0). Folder: {baseDir}");

        width = meta.width;
        height = meta.height;
        frames = meta.frames;

        cellSizeMeters = meta.scaleCm / 100f;
        frameSeconds = meta.frameSeconds;
        AppManager.Instance.Session.TimeEvolutionSpeed = frameSeconds;

        interp2D = ParseInterp2D(meta.interp2D);
        interp3D = ParseInterp3D(meta.interp3D);
        frameMode = ParseFrameMode(meta.frameMode);

        scaleParameter = meta.scaleParameter;
        brightnessMaxPercent = meta.brightnessMaxPercent;

        if (!string.Equals(meta.dataEncoding, "u8_raw", StringComparison.OrdinalIgnoreCase))
            throw new Exception($"Unsupported dataEncoding '{meta.dataEncoding}'. Only 'u8_raw' supported currently.");

        string dataPath = Path.Combine(baseDir, string.IsNullOrWhiteSpace(meta.dataFile) ? "data.bin" : meta.dataFile);
        if (!File.Exists(dataPath))
            throw new FileNotFoundException($"Matrix data file not found: {dataPath}");

        data = File.ReadAllBytes(dataPath);

        long expected = (long)width * height * frames;
        if (data.LongLength != expected)
        {
            throw new Exception(
                $"Matrix data size mismatch in '{dataPath}'. Expected {expected} bytes " +
                $"(width={width}, height={height}, frames={frames}), got {data.LongLength} bytes.");
        }
    }

    public float Evaluate(Vector2 pos)
    {
        if (valid == false) return 0;
        
        if (!clockStarted)
        {
            clockStarted = true;
            t0 = Time.time;
        }

        // World -> continuous grid coords
        float dx = pos.x - center.x;
        float dy = pos.y - center.y;

        float gx = dx / cellSizeMeters + (width - 1) * 0.5f;
        float gy = dy / cellSizeMeters + (height - 1) * 0.5f;

        // Out of bounds -> 0
        if (gx < 0f || gy < 0f || gx > (width - 1) || gy > (height - 1))
            return 0f;

        // Time -> frame coordinate
        float elapsed = Time.time - t0;
        float ft = elapsed / frameSeconds;
        int baseFrame = Mathf.FloorToInt(ft);
        float alpha = ft - baseFrame;

        int f0 = ResolveFrame(baseFrame);
        if (interp3D == Interp3D.None || frames == 1)
        {
            return Sample2D(gx, gy, f0);
        }

        int f1 = ResolveFrame(baseFrame + 1);
        float a0 = Sample2D(gx, gy, f0);
        float a1 = Sample2D(gx, gy, f1);
        return Mathf.Lerp(a0, a1, Mathf.Clamp01(alpha));
    }

    public Vector2 GetPrimaryTarget() => center;

    // -------------------------
    // Sampling
    // -------------------------

    private float Sample2D(float gx, float gy, int frame)
    {
        if (interp2D == Interp2D.Nearest)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt(gx + 0.5f), 0, width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(gy + 0.5f), 0, height - 1);
            return ByteTo01(Read(frame, x, y));
        }
        else // Bilinear
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(gy), 0, height - 1);
            int x1 = Mathf.Clamp(x0 + 1, 0, width - 1);
            int y1 = Mathf.Clamp(y0 + 1, 0, height - 1);

            float tx = gx - x0;
            float ty = gy - y0;

            float v00 = ByteTo01(Read(frame, x0, y0));
            float v10 = ByteTo01(Read(frame, x1, y0));
            float v01 = ByteTo01(Read(frame, x0, y1));
            float v11 = ByteTo01(Read(frame, x1, y1));

            float a = Mathf.Lerp(v00, v10, tx);
            float b = Mathf.Lerp(v01, v11, tx);
            return Mathf.Lerp(a, b, ty);
        }
    }

    private byte Read(int frame, int x, int y)
    {
        // flat index: (frame * height + y) * width + x
        int idx = (frame * height + y) * width + x;
        return data[idx];
    }

    private static float ByteTo01(byte b) => b / 255f;

    // -------------------------
    // Frame resolution
    // -------------------------

    private int ResolveFrame(int frame)
    {
        if (frames <= 1) return 0;

        if (frameMode == FrameMode.Clamp)
        {
            return Mathf.Clamp(frame, 0, frames - 1);
        }

        // Loop
        int m = frames;
        int r = frame % m;
        if (r < 0) r += m;
        return r;
    }

    // -------------------------
    // Meta parsing helpers
    // -------------------------

    private static Interp2D ParseInterp2D(string s)
    {
        if (string.Equals(s, "bilinear", StringComparison.OrdinalIgnoreCase)) return Interp2D.Bilinear;
        return Interp2D.Nearest;
    }

    private static Interp3D ParseInterp3D(string s)
    {
        if (string.Equals(s, "linear", StringComparison.OrdinalIgnoreCase)) return Interp3D.Linear;
        return Interp3D.None; // none => floor frame (handled by baseFrame)
    }

    private static FrameMode ParseFrameMode(string s)
    {
        if (string.Equals(s, "loop", StringComparison.OrdinalIgnoreCase)) return FrameMode.Loop;
        return FrameMode.Clamp;
    }

    public float ScaleParameter => scaleParameter;
    public float BrightnessMaxPercent => brightnessMaxPercent;
}