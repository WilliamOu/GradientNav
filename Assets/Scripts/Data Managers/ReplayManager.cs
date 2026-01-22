using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class ReplayManager : MonoBehaviour
{
    // --- Restored Constants ---
    public const int XriBinVersion = 2;
    public const int XriBinMagic = 0x58524942;
    public const int ShadowBinMagic = 0x53484457;

    public enum AxisPermutation
    {
        XYZ, XZY, YXZ, YZX, ZXY, ZYX
    }

    [Header("Import Settings")]
    [Tooltip("Cycle this if the rig looks flat or sideways.")]
    public AxisPermutation axisMapping = AxisPermutation.XYZ;
    [Tooltip("Usually 0.01 for cm -> meters.")]
    public float importScale = 0.01f;
    [Tooltip("Flip the X axis? (Fixes mirroring).")]
    public bool negateX = false;

    [Header("Anchoring")]
    [Tooltip("Which bone index is the Head? Scroll this until the Green dot snaps to the headset.")]
    [Range(0, 16)] public int anchorBoneIndex = 1; // Pretty sure 1 is the head
    [Tooltip("Offset from Shadow Head bone to XRI Headset (center of skull).")]
    public Vector3 shadowHeadToSkullOffset = new Vector3(0, 0, 0.08f);

    [Header("Playback")]
    [Range(0.1f, 5f)] public float playbackSpeed = 1.0f;
    public bool drawSkeletonLines = true;

    [Header("Visuals")]
    [SerializeField] private float dotSize = 0.05f;
    public Material shadowDotMaterial;
    public Material xriProxyMaterial;

    // --- Read-only Debug ---
    [SerializeField] private string loadedFolderPath;
    [SerializeField] private float currentReplayTime = 0f;
    [SerializeField] private float totalDuration = 0f;
    [SerializeField] private bool isPlaying = false;

    // --- Data ---
    private List<XriFrame> xriData = new List<XriFrame>();
    private List<ShadowFrame> shadowData = new List<ShadowFrame>();

    // --- Scene References ---
    private Transform shadowRoot;
    private Transform[] shadowDots;
    private Transform xriHead, xriLeft, xriRight;

    // IDs
    private const byte DevHead = 0;
    private const byte DevLeft = 1;
    private const byte DevRight = 2;
    private const byte DevGaze = 3;
    private const byte ShadowBoneCount = 17;

    // Freeze Thresholds
    private const float ShadowFreezeThreshold = 0.04f;
    private float XriFreezeThreshold = 0.044f;

    // --- Restored Init ---
    public void Init(Material shadowDotMaterial, Material xriProxyMaterial)
    {
        this.shadowDotMaterial = shadowDotMaterial;
        this.xriProxyMaterial = xriProxyMaterial;
    }

    public void SetFolderPath(string folderPath)
    {
        loadedFolderPath = folderPath;
    }

    private void Update()
    {
        if (!isPlaying) return;

        currentReplayTime += Time.deltaTime * playbackSpeed;

        if (currentReplayTime > totalDuration)
        {
            currentReplayTime = 0f; // Loop
        }

        EvaluateXri(currentReplayTime);
        EvaluateShadow(currentReplayTime);
        AnchorShadowToHeadset();

        if (drawSkeletonLines) DrawSkeleton();
        DrawDebugGizmos();
    }

    public void BeginReplayFromFolder()
    {
        StopReplay();

        try
        {
            if (string.IsNullOrEmpty(loadedFolderPath))
            {
                Debug.LogError("[ReplayManager] No folder path set.");
                return;
            }

            string[] xriFiles = Directory.GetFiles(loadedFolderPath, "*_XRI.bin");
            string[] shadowFiles = Directory.GetFiles(loadedFolderPath, "*_Shadow.bin");

            if (xriFiles.Length == 0 || shadowFiles.Length == 0)
            {
                Debug.LogError("[ReplayManager] Missing .bin files.");
                return;
            }

            // Init thresholds
            if (AppManager.Instance != null)
            {
                float interval = (float)AppManager.Instance.Settings.DataLogInterval;
                if (interval <= 0) interval = 1f / 90f;
                XriFreezeThreshold = interval * 4f;
            }

            long minTick = long.MaxValue;
            long maxTick = long.MinValue;
            long fileFreq = System.Diagnostics.Stopwatch.Frequency;

            LoadShadowBin(shadowFiles[0], ref minTick, ref maxTick, ref fileFreq);
            LoadXriBin(xriFiles[0], ref minTick, ref maxTick);

            if (xriData.Count == 0 || shadowData.Count == 0)
            {
                Debug.LogError("[ReplayManager] Data loaded but empty.");
                return;
            }

            NormalizeTimestamps(minTick, fileFreq);
            totalDuration = (float)((double)(maxTick - minTick) / fileFreq);

            SpawnVisuals();

            currentReplayTime = 0f;
            isPlaying = true;
            Debug.Log($"[ReplayManager] Playing {totalDuration:F1}s.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[ReplayManager] Load Error: {e.Message}\n{e.StackTrace}");
        }
    }

    public void StopReplay()
    {
        isPlaying = false;
        xriData.Clear();
        shadowData.Clear();
        if (shadowRoot) Destroy(shadowRoot.gameObject);
        if (xriHead) Destroy(xriHead.gameObject);
        if (xriLeft) Destroy(xriLeft.gameObject);
        if (xriRight) Destroy(xriRight.gameObject);
    }

    // --- Loading ---

    private void LoadXriBin(string path, ref long minTick, ref long maxTick)
    {
        using var br = new BinaryReader(File.OpenRead(path));
        if (br.ReadInt32() != XriBinMagic) throw new Exception("Invalid XRI Magic");
        br.ReadInt32(); // Version
        int count = br.ReadInt32();
        bool hasState = (br.ReadByte() & 1) != 0;

        for (int i = 0; i < count; i++)
        {
            var f = new XriFrame();
            f.Ticks = br.ReadInt64();
            if (hasState) br.ReadByte();

            for (int d = 0; d < 4; d++)
            {
                byte id = br.ReadByte();
                Vector3 v = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

                if (id == DevGaze)
                {
                    f.GazeOrigin = v;
                    f.GazeDirection = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                }
                else
                {
                    Quaternion q = new Quaternion(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    if (id == DevHead) { f.HeadPos = v; f.HeadRot = q; }
                    else if (id == DevLeft) { f.LPos = v; f.LRot = q; }
                    else if (id == DevRight) { f.RPos = v; f.RRot = q; }
                }
            }
            xriData.Add(f);
            if (f.Ticks < minTick) minTick = f.Ticks;
            if (f.Ticks > maxTick) maxTick = f.Ticks;
        }
    }

    private void LoadShadowBin(string path, ref long minTick, ref long maxTick, ref long fileFreq)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var br = new BinaryReader(fs);

        if (br.ReadInt32() != ShadowBinMagic) throw new Exception("Invalid Shadow Magic");
        br.ReadInt32(); // Ver
        br.ReadInt32(); // BoneCount
        fileFreq = br.ReadInt64();

        // Skip header padding
        br.BaseStream.Seek(80, SeekOrigin.Current);
        long len = br.BaseStream.Length;

        while (br.BaseStream.Position < len)
        {
            var f = new ShadowFrame();
            f.Ticks = br.ReadInt64();
            br.BaseStream.Seek(8, SeekOrigin.Current); // Skip UnityTime

            for (int b = 0; b < ShadowBoneCount; b++)
            {
                // Store RAW values.
                float x = br.ReadSingle();
                float y = br.ReadSingle();
                float z = br.ReadSingle();
                f.SetBone(b, new Vector3(x, y, z));

                // Skip Rotation
                br.ReadSingle(); br.ReadSingle(); br.ReadSingle(); br.ReadSingle();
            }
            shadowData.Add(f);

            if (f.Ticks < minTick) minTick = f.Ticks;
            if (f.Ticks > maxTick) maxTick = f.Ticks;
        }
    }

    private void NormalizeTimestamps(long start, long freq)
    {
        double frequency = (double)freq;
        foreach (var f in xriData) f.Time = (float)((f.Ticks - start) / frequency);
        foreach (var f in shadowData) f.Time = (float)((f.Ticks - start) / frequency);
    }

    // --- Evaluation & Transformation ---

    private void EvaluateShadow(float time)
    {
        var (a, b, t) = FindFrame(shadowData, time);

        if (b.Time - a.Time > ShadowFreezeThreshold) t = 0f;

        for (int i = 0; i < ShadowBoneCount; i++)
        {
            if (shadowDots[i] != null)
            {
                Vector3 raw = Vector3.Lerp(a.Positions[i], b.Positions[i], t);

                // 1. Scale
                raw *= importScale;

                // 2. Swizzle
                Vector3 p = MapAxis(raw, axisMapping);

                // 3. Negate
                if (negateX) p.x = -p.x;

                shadowDots[i].localPosition = p;
            }
        }
    }

    private Vector3 MapAxis(Vector3 v, AxisPermutation map)
    {
        switch (map)
        {
            case AxisPermutation.XYZ: return new Vector3(v.x, v.y, v.z);
            case AxisPermutation.XZY: return new Vector3(v.x, v.z, v.y);
            case AxisPermutation.YXZ: return new Vector3(v.y, v.x, v.z);
            case AxisPermutation.YZX: return new Vector3(v.y, v.z, v.x);
            case AxisPermutation.ZXY: return new Vector3(v.z, v.x, v.y);
            case AxisPermutation.ZYX: return new Vector3(v.z, v.y, v.x);
            default: return v;
        }
    }

    private void EvaluateXri(float time)
    {
        var (a, b, t) = FindFrame(xriData, time);
        if (b.Time - a.Time > XriFreezeThreshold) t = 0f;

        if (xriHead)
        {
            xriHead.localPosition = Vector3.Lerp(a.HeadPos, b.HeadPos, t);
            xriHead.localRotation = Quaternion.Slerp(a.HeadRot, b.HeadRot, t);
        }
        if (xriLeft) xriLeft.localPosition = Vector3.Lerp(a.LPos, b.LPos, t);
        if (xriRight) xriRight.localPosition = Vector3.Lerp(a.RPos, b.RPos, t);

        currentGazeOrigin = Vector3.Lerp(a.GazeOrigin, b.GazeOrigin, t);
        currentGazeDir = Vector3.Slerp(a.GazeDirection, b.GazeDirection, t);
    }

    private (T, T, float) FindFrame<T>(List<T> list, float time) where T : IFrame
    {
        if (list.Count == 0) return (default, default, 0);
        int idx = 0;
        for (int i = 0; i < list.Count - 1; i++)
        {
            if (time >= list[i].Time && time < list[i + 1].Time)
            {
                idx = i; break;
            }
        }
        var a = list[idx];
        var b = (idx + 1 < list.Count) ? list[idx + 1] : a;
        float gap = b.Time - a.Time;
        float t = gap > 1e-5f ? (time - a.Time) / gap : 0f;
        return (a, b, t);
    }

    // --- Visualization ---

    private Vector3 currentGazeOrigin, currentGazeDir;

    private void DrawDebugGizmos()
    {
        if (currentGazeDir != Vector3.zero && xriHead != null)
        {
            Debug.DrawRay(currentGazeOrigin, currentGazeDir * 2f, Color.green);
            Debug.DrawRay(xriHead.position, xriHead.forward * 1f, Color.blue);
        }
    }

    private void DrawSkeleton()
    {
        // Simple connectivity guess. Works if Hips=0, Head=2, etc. 
        // If sorting is weird, this will look like a spider web, helping diagnose the sort.
        // Connecting blindly 0->1->2... often reveals the structure too.
        if (shadowDots == null || shadowDots.Length == 0) return;

        for (int i = 0; i < shadowDots.Length - 1; i++)
        {
            if (shadowDots[i] != null && shadowDots[i + 1] != null)
                Debug.DrawLine(shadowDots[i].position, shadowDots[i + 1].position, Color.white);
        }
    }

    private void AnchorShadowToHeadset()
    {
        if (xriHead == null || shadowDots == null || shadowDots.Length == 0) return;

        // Safety check for user-selected index
        int index = Mathf.Clamp(anchorBoneIndex, 0, ShadowBoneCount - 1);
        Transform anchor = shadowDots[index];

        if (anchor == null) return;

        // XRI Head position - offset (rotated into head space)
        Vector3 target = xriHead.position - (xriHead.rotation * shadowHeadToSkullOffset);

        // Move the whole cloud so that anchor matches target
        shadowRoot.position += (target - anchor.position);
    }

    private void SpawnVisuals()
    {
        if (shadowRoot) Destroy(shadowRoot.gameObject);
        shadowRoot = new GameObject("ShadowCloud").transform;
        shadowDots = new Transform[ShadowBoneCount];

        for (int i = 0; i < ShadowBoneCount; i++)
        {
            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(dot.GetComponent<Collider>());

            var r = dot.GetComponent<Renderer>();
            if (shadowDotMaterial) r.material = shadowDotMaterial;
            r.material.color = Color.red;

            dot.transform.SetParent(shadowRoot);
            dot.transform.localScale = Vector3.one * dotSize;
            dot.name = $"Bone_{i}"; // Name by index since we don't know mapping
            shadowDots[i] = dot.transform;
        }

        xriHead = CreateProxy("XRI_Head", 0.15f);
        xriLeft = CreateProxy("XRI_L", 0.08f);
        xriRight = CreateProxy("XRI_R", 0.08f);
    }

    private Transform CreateProxy(string name, float scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(go.GetComponent<Collider>());
        go.name = name;
        go.transform.localScale = Vector3.one * scale;
        if (xriProxyMaterial) go.GetComponent<Renderer>().material = xriProxyMaterial;
        return go.transform;
    }

    private interface IFrame { float Time { get; set; } }

    private class XriFrame : IFrame
    {
        public long Ticks; public float Time { get; set; }
        public Vector3 HeadPos, LPos, RPos;
        public Quaternion HeadRot, LRot, RRot;
        public Vector3 GazeOrigin, GazeDirection;
    }

    private class ShadowFrame : IFrame
    {
        public long Ticks; public float Time { get; set; }
        public Vector3[] Positions = new Vector3[17];
        public void SetBone(int i, Vector3 p) { Positions[i] = p; }
    }
}