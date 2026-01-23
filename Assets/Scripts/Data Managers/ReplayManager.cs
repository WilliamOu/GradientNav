using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class ReplayManager : MonoBehaviour
{
    // --- Constants ---
    public const int XriBinVersion = 2;
    public const int XriBinMagic = 0x58524942;
    public const int ShadowBinMagic = 0x53484457;

    public enum AxisPermutation { XYZ, XZY, YXZ, YZX, ZXY, ZYX }

    [Header("Import Settings")]
    public AxisPermutation axisMapping = AxisPermutation.XYZ;
    public float importScale = 0.01f;
    public bool negateX = false;

    [Header("Anchoring & Calibration")]
    [Tooltip("Which bone index is the Head?")]
    [Range(0, 16)] public int headIndex = 1;
    [Tooltip("Offset from Shadow Head bone to XRI Headset.")]
    public Vector3 shadowHeadToSkullOffset = new Vector3(0, 0, 0);

    [Space(10)]
    [Tooltip("Manual rotation adjustment around the vertical axis.")]
    [Range(0f, 360f)] public float yawCorrection = 0f;

    [Header("Auto-Align Settings")]
    public bool autoAlignOnStart = true;
    [Tooltip("Index of Shadow Left Hand (Use Show Labels to find this).")]
    public int shadowLeftHandIndex = 13;
    [Tooltip("Index of Shadow Right Hand (Use Show Labels to find this).")]
    public int shadowRightHandIndex = 6;
    public bool continuousAutoAlign = true;

    [Header("Playback")]
    [Range(0.1f, 5f)] public float playbackSpeed = 1.0f;
    public bool showLabels = false;
    public bool drawSkeletonLines = false;

    [Header("Visuals")]
    [SerializeField] private float dotSize = 0.05f;
    public Material shadowDotMaterial;
    public Material xriProxyMaterial;

    // --- Runtime ---
    [SerializeField] private string loadedFolderPath;
    [SerializeField] private float currentReplayTime = 0f;
    [SerializeField] private float totalDuration = 0f;
    [SerializeField] private bool isPlaying = false;

    // RESTORED VARIABLES
    private Vector3 currentGazeOrigin, currentGazeDir;

    // --- Data ---
    private List<XriFrame> xriData = new List<XriFrame>();
    private List<ShadowFrame> shadowData = new List<ShadowFrame>();

    // --- Scene Objects ---
    private Transform shadowRoot;
    private Transform[] shadowDots;
    private TextMesh[] dotLabels;
    private Transform xriHead, xriLeft, xriRight;

    // --- IDs ---
    private const byte DevHead = 0;
    private const byte DevLeft = 1;
    private const byte DevRight = 2;
    private const byte DevGaze = 3;
    private const byte ShadowBoneCount = 17;

    // Freeze Thresholds
    private const float ShadowFreezeThreshold = 0.04f;
    private float XriFreezeThreshold = 0.044f;

    public void Init(Material shadowDotMaterial, Material xriProxyMaterial)
    {
        this.shadowDotMaterial = shadowDotMaterial;
        this.xriProxyMaterial = xriProxyMaterial;
    }

    public void SetFolderPath(string folderPath) { loadedFolderPath = folderPath; }

    private void Update()
    {
        if (!isPlaying) return;

        currentReplayTime += Time.deltaTime * playbackSpeed;
        if (currentReplayTime > totalDuration) currentReplayTime = 0f;

        EvaluateXri(currentReplayTime);
        EvaluateShadow(currentReplayTime);

        // 1. Apply Anchor (Position)
        AnchorShadowPosition();

        // 2. Apply Rotation (Manual + Auto)
        ApplyShadowRotation();

        // 3. Visualization
        UpdateLabels();
        if (drawSkeletonLines) DrawSkeleton();
        DrawDebugGizmos();
    }

    // --- Math: The Closed Form Solution ---
    [ContextMenu("Calculate Auto-Align Yaw")]
    public void CalculateAutoAlignYaw()
    {
        if (xriHead == null || shadowDots == null) return;

        // Ensure indices are valid
        if (shadowLeftHandIndex >= shadowDots.Length || shadowRightHandIndex >= shadowDots.Length) return;

        // 1. Get XRI Vectors (Head -> Hand) on the flat floor plane (XZ)
        Vector3 xriL = Vector3.ProjectOnPlane(xriLeft.position - xriHead.position, Vector3.up);
        Vector3 xriR = Vector3.ProjectOnPlane(xriRight.position - xriHead.position, Vector3.up);

        // 2. Get Shadow Vectors (Head -> Hand) in LOCAL space (un-rotated)
        Vector3 sHeadPos = shadowDots[headIndex].localPosition;
        Vector3 sLPos = shadowDots[shadowLeftHandIndex].localPosition;
        Vector3 sRPos = shadowDots[shadowRightHandIndex].localPosition;

        Vector3 shadowL = Vector3.ProjectOnPlane(sLPos - sHeadPos, Vector3.up);
        Vector3 shadowR = Vector3.ProjectOnPlane(sRPos - sHeadPos, Vector3.up);

        // 3. Solve for Theta
        float crossSum = (shadowL.z * xriL.x - shadowL.x * xriL.z) + (shadowR.z * xriR.x - shadowR.x * xriR.z);
        float dotSum = (shadowL.x * xriL.x + shadowL.z * xriL.z) + (shadowR.x * xriR.x + shadowR.z * xriR.z);

        float thetaRad = Mathf.Atan2(crossSum, dotSum);
        float thetaDeg = thetaRad * Mathf.Rad2Deg;

        yawCorrection = thetaDeg;
        if (yawCorrection < 0) yawCorrection += 360f;

        Debug.Log($"[AutoAlign] Calculated Offset: {yawCorrection:F1} degrees");
    }

    private void ApplyShadowRotation()
    {
        if (continuousAutoAlign) CalculateAutoAlignYaw();

        if (shadowRoot != null && xriHead != null)
        {
            shadowRoot.rotation = Quaternion.Euler(0, yawCorrection, 0);
            AnchorShadowPosition();
        }
    }

    private void AnchorShadowPosition()
    {
        if (xriHead == null || shadowDots == null) return;

        Transform shadowHead = shadowDots[headIndex];
        if (shadowHead == null) return;

        Vector3 targetPos = xriHead.position - (xriHead.rotation * shadowHeadToSkullOffset);
        Vector3 currentHeadWorld = shadowHead.position;
        Vector3 delta = targetPos - currentHeadWorld;

        shadowRoot.position += delta;
    }

    // --- Visualization Updates ---

    private void UpdateLabels()
    {
        if (dotLabels == null) return;

        for (int i = 0; i < dotLabels.Length; i++)
        {
            if (dotLabels[i] == null) continue;

            if (!showLabels)
            {
                if (dotLabels[i].gameObject.activeSelf) dotLabels[i].gameObject.SetActive(false);
                continue;
            }

            if (!dotLabels[i].gameObject.activeSelf) dotLabels[i].gameObject.SetActive(true);
            dotLabels[i].transform.position = shadowDots[i].position + Vector3.up * 0.1f;
            dotLabels[i].transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward);
        }
    }

    // --- Standard Load/Eval Logic ---

    public void BeginReplayFromFolder()
    {
        StopReplay();
        try
        {
            string[] xriFiles = Directory.GetFiles(loadedFolderPath, "*_XRI.bin");
            string[] shadowFiles = Directory.GetFiles(loadedFolderPath, "*_Shadow.bin");
            if (xriFiles.Length == 0 || shadowFiles.Length == 0) return;

            long minTick = long.MaxValue;
            long maxTick = long.MinValue;
            long fileFreq = System.Diagnostics.Stopwatch.Frequency;

            LoadShadowBin(shadowFiles[0], ref minTick, ref maxTick, ref fileFreq);
            LoadXriBin(xriFiles[0], ref minTick, ref maxTick);
            NormalizeTimestamps(minTick, fileFreq);

            if (shadowData.Count > 0) totalDuration = shadowData[shadowData.Count - 1].Time;

            SpawnVisuals();
            currentReplayTime = 0f;

            // --- AUTO ALIGN START ---
            EvaluateXri(0f);
            EvaluateShadow(0f);

            if (autoAlignOnStart)
            {
                CalculateAutoAlignYaw();
                ApplyShadowRotation();
            }
            // ------------------------

            isPlaying = true;
        }
        catch (Exception e) { Debug.LogError(e); }
    }

    public void StopReplay()
    {
        isPlaying = false;
        xriData.Clear(); shadowData.Clear();
        if (shadowRoot) Destroy(shadowRoot.gameObject);
        if (xriHead) Destroy(xriHead.gameObject);
        if (xriLeft) Destroy(xriLeft.gameObject);
        if (xriRight) Destroy(xriRight.gameObject);
    }

    private void LoadXriBin(string path, ref long minTick, ref long maxTick)
    {
        using var br = new BinaryReader(File.OpenRead(path));
        br.ReadInt32(); br.ReadInt32();
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
                if (id == DevGaze) { f.GazeOrigin = v; f.GazeDirection = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()); }
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
        br.ReadInt32(); br.ReadInt32(); br.ReadInt32();
        fileFreq = br.ReadInt64();
        br.BaseStream.Seek(80, SeekOrigin.Current);
        long len = br.BaseStream.Length;

        while (br.BaseStream.Position < len)
        {
            var f = new ShadowFrame();
            f.Ticks = br.ReadInt64();
            br.BaseStream.Seek(8, SeekOrigin.Current);
            for (int b = 0; b < ShadowBoneCount; b++)
            {
                float x = br.ReadSingle(); float y = br.ReadSingle(); float z = br.ReadSingle();
                f.SetBone(b, new Vector3(x, y, z));
                br.ReadSingle(); br.ReadSingle(); br.ReadSingle(); br.ReadSingle();
            }
            shadowData.Add(f);
            if (f.Ticks < minTick) minTick = f.Ticks;
            if (f.Ticks > maxTick) maxTick = f.Ticks;
        }
    }

    private void NormalizeTimestamps(long start, long freq)
    {
        double f = (double)freq;
        foreach (var fr in xriData) fr.Time = (float)((fr.Ticks - start) / f);
        foreach (var fr in shadowData) fr.Time = (float)((fr.Ticks - start) / f);
    }

    private void EvaluateShadow(float time)
    {
        var (a, b, t) = FindFrame(shadowData, time);
        if (b.Time - a.Time > ShadowFreezeThreshold) t = 0f;

        for (int i = 0; i < ShadowBoneCount; i++)
        {
            if (shadowDots[i] != null)
            {
                Vector3 raw = Vector3.Lerp(a.Positions[i], b.Positions[i], t);
                raw *= importScale;
                Vector3 p = MapAxis(raw, axisMapping);
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
            if (time >= list[i].Time && time < list[i + 1].Time) { idx = i; break; }
        }
        var a = list[idx];
        var b = (idx + 1 < list.Count) ? list[idx + 1] : a;
        float gap = b.Time - a.Time;
        float t = gap > 1e-5f ? (time - a.Time) / gap : 0f;
        return (a, b, t);
    }

    private void SpawnVisuals()
    {
        if (shadowRoot) Destroy(shadowRoot.gameObject);
        shadowRoot = new GameObject("ShadowCloud").transform;
        shadowDots = new Transform[ShadowBoneCount];
        dotLabels = new TextMesh[ShadowBoneCount];

        for (int i = 0; i < ShadowBoneCount; i++)
        {
            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(dot.GetComponent<Collider>());
            if (shadowDotMaterial) dot.GetComponent<Renderer>().material = shadowDotMaterial;
            dot.transform.SetParent(shadowRoot);
            dot.transform.localScale = Vector3.one * dotSize;
            dot.name = $"Bone_{i}";
            shadowDots[i] = dot.transform;

            // Spawn Label
            GameObject labelObj = new GameObject($"Label_{i}");
            labelObj.transform.SetParent(shadowRoot);
            labelObj.SetActive(showLabels);
            TextMesh tm = labelObj.AddComponent<TextMesh>();
            tm.text = i.ToString();
            tm.characterSize = 0.1f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = Color.white;
            dotLabels[i] = tm;
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
        if (shadowDots == null) return;
        for (int i = 0; i < shadowDots.Length - 1; i++)
            if (shadowDots[i] && shadowDots[i + 1])
                Debug.DrawLine(shadowDots[i].position, shadowDots[i + 1].position, Color.gray);
    }

    private interface IFrame { float Time { get; set; } }
    private class XriFrame : IFrame { public long Ticks; public float Time { get; set; } public Vector3 HeadPos, LPos, RPos; public Quaternion HeadRot, LRot, RRot; public Vector3 GazeOrigin, GazeDirection; }
    private class ShadowFrame : IFrame { public long Ticks; public float Time { get; set; } public Vector3[] Positions = new Vector3[17]; public void SetBone(int i, Vector3 p) { Positions[i] = p; } }
}