using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/*
Limbs:
Chest: 0
Head: 1
Pelvis: 2
Right Shoulder/Upper Arm: 3
Right Foot: 4
Right Elbow: 5
Right Hand: 6
Right Knee: 7
Right Collarbone: 8
Right Upper Leg: 9
Left Shoulder/Upper Arm: 10
Left Foot: 11
Left Elbow: 12
Left Hand: 13
Left Knee: 14
Left Collarbone: 15
Left Upper Leg: 16
*/

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
    [Range(0, 16)] public int headIndex = 1;
    public Vector3 shadowHeadToSkullOffset = Vector3.zero;
    [Range(0f, 360f)] public float yawCorrection = 0f;

    [Header("Auto-Align")]
    private bool autoAlignOnStart = false;
    public int shadowLeftHandIndex = 13;
    public int shadowRightHandIndex = 6;
    private bool continuousAutoAlign = false;

    [Header("Playback Controls")]
    [Range(0.1f, 5f)] public float playbackSpeed = 1.0f;
    public float maxSpeed = 4.0f;
    public float minSpeed = 0.25f;
    public bool showLabels = false;
    public bool drawSkeletonLines = false;

    [Header("Visuals")]
    [SerializeField] private float dotSize = 0.05f;
    public Material shadowDotMaterial;
    public Material xriProxyMaterial;

    [Header("File System")]
    [SerializeField] private string loadedFolderPath;

    // --- State ---
    private bool isPlaying = false;
    private float currentTime = 0f;
    private float totalDuration = 0f;
    private float frameStepSize = 0.033f;

    // --- Data Streamers ---
    private XriStreamer xriStream;
    private ShadowStreamer shadowStream;

    // --- Scene Objects ---
    private Transform shadowRoot;
    private Transform[] shadowDots;
    private TextMesh[] dotLabels;
    private Transform xriHead, xriLeft, xriRight;
    private Vector3 currentGazeOrigin, currentGazeDir;

    // --- Bone IDs ---
    private const int ShadowBoneCount = 17;

    public float CurrentTime => currentTime;
    public float PlaybackSpeed => playbackSpeed;
    public bool ContinuousAutoAlign => continuousAutoAlign;
    public float MaxTime => totalDuration;
    public bool IsPlaying => isPlaying;

    public (XriFrame frameA, XriFrame frameB, float t) GetCurrentXriFrames()
    {
        if (xriStream == null) return (null, null, 0f);
        return xriStream.GetFrame(currentTime);
    }

    public void TogglePlayPause()
    {
        isPlaying = !isPlaying;
    }

    public void SetPlayState(bool play)
    {
        isPlaying = play;
    }

    public void IncreasePlaybackSpeed()
    {
        playbackSpeed = Mathf.Min(playbackSpeed + 0.25f, maxSpeed);
    }

    public void DecreasePlaybackSpeed()
    {
        playbackSpeed = Mathf.Max(playbackSpeed - 0.25f, minSpeed);
    }

    public void StepFrameForward()
    {
        isPlaying = false; // Stepping usually pauses playback
        SetTime(currentTime + frameStepSize);
    }

    public void StepFrameBack()
    {
        isPlaying = false;
        SetTime(currentTime - frameStepSize);
    }

    public Transform GetXriHeadTransform()
    {
        return xriHead;
    }

    public void SetTime(float time)
    {
        // Clamp and Set
        currentTime = Mathf.Clamp(time, 0f, totalDuration);

        // Force an immediate evaluation
        EvaluateAtTime(currentTime);
    }

    public void ToggleAutoAlign()
    {
        continuousAutoAlign = !continuousAutoAlign;
    }

    // --- Unity Events ---

    public void SetFolderPath(string folderPath) { loadedFolderPath = folderPath; }
    public string GetFolderPath() { return loadedFolderPath; }

    public void Init(Material shadowDotMaterial, Material xriProxyMaterial)
    {
        this.shadowDotMaterial = shadowDotMaterial;
        this.xriProxyMaterial = xriProxyMaterial;
    }

    private void OnDestroy()
    {
        CloseStreams();
    }

    private void Update()
    {
        if (isPlaying)
        {
            currentTime += Time.deltaTime * playbackSpeed;

            // Loop logic
            if (currentTime >= totalDuration)
            {
                currentTime = 0f;
            }

            EvaluateAtTime(currentTime);
        }

        // Always run visuals updates (in case of seeking while paused)
        if (shadowRoot != null)
        {
            AnchorShadowPosition();
            ApplyShadowRotation();
            UpdateLabels();
            if (drawSkeletonLines) DrawSkeleton();
            DrawDebugGizmos();
        }
    }

    // --- Core Logic ---

    public void BeginReplayFromFolder()
    {
        CloseStreams();

        try
        {
            string[] xriFiles = Directory.GetFiles(loadedFolderPath, "*_XRI.bin");
            string[] shadowFiles = Directory.GetFiles(loadedFolderPath, "*_Shadow.bin");

            if (xriFiles.Length == 0 || shadowFiles.Length == 0)
            {
                Debug.LogError("ReplayManager: Missing XRI or Shadow files in folder.");
                return;
            }

            // Open Streams & Build Indices
            // This reads the whole file structure but skips the heavy data, building a map in RAM.
            xriStream = new XriStreamer(xriFiles[0]);
            shadowStream = new ShadowStreamer(shadowFiles[0]);

            // Sync Time Bases
            // We align both streams to the earliest timestamp found.
            long startTick = Math.Min(xriStream.StartTick, shadowStream.StartTick);
            long freq = shadowStream.Frequency; // Shadow usually has the reliable frequency

            if (freq == 0) freq = System.Diagnostics.Stopwatch.Frequency;

            xriStream.SetTiming(startTick, freq);
            shadowStream.SetTiming(startTick, freq);

            // Setup Duration & Steps
            totalDuration = Mathf.Max(xriStream.Duration, shadowStream.Duration);

            // Calculate a single "Frame" as the time between two shadow samples
            if (shadowStream.FrameCount > 1)
                frameStepSize = shadowStream.Duration / shadowStream.FrameCount;

            Debug.Log($"[Replay] Loaded. Duration: {totalDuration:F2}s. Frame Step: {frameStepSize:F4}s");

            // Spawn & Reset
            SpawnVisuals();
            SetTime(0f);

            if (autoAlignOnStart)
            {
                CalculateAutoAlignYaw();
                ApplyShadowRotation();
            }

            isPlaying = true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Replay] Error Loading: {e.Message}");
            CloseStreams();
        }
    }

    private void CloseStreams()
    {
        isPlaying = false;
        xriStream?.Dispose(); xriStream = null;
        shadowStream?.Dispose(); shadowStream = null;

        if (shadowRoot) Destroy(shadowRoot.gameObject);
        if (xriHead) Destroy(xriHead.gameObject);
        if (xriLeft) Destroy(xriLeft.gameObject);
        if (xriRight) Destroy(xriRight.gameObject);
    }

    private void EvaluateAtTime(float time)
    {
        // Evaluate Shadow (Body)
        if (shadowStream != null)
        {
            // This function handles the file seeking and interpolation
            var (a, b, t) = shadowStream.GetFrame(time);

            // Check for "Freeze" (if gap is too large, snap to A)
            if (b.Time - a.Time > 0.1f) t = 0f;

            for (int i = 0; i < ShadowBoneCount; i++)
            {
                if (shadowDots[i] != null)
                {
                    Vector3 raw = Vector3.Lerp(a.Positions[i], b.Positions[i], t);

                    // Apply Import Settings
                    raw *= importScale;
                    Vector3 p = MapAxis(raw, axisMapping);
                    if (negateX) p.x = -p.x;

                    shadowDots[i].localPosition = p;
                }
            }
        }

        // Evaluate XRI (Headset/Controllers)
        if (xriStream != null)
        {
            var (a, b, t) = xriStream.GetFrame(time);
            if (b.Time - a.Time > 0.1f) t = 0f;

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
    }

    // --- Math & Visuals Helpers (Unchanged Logic) ---

    [ContextMenu("Calculate Auto-Align Yaw")]
    public void CalculateAutoAlignYaw()
    {
        if (xriHead == null || shadowDots == null) return;
        if (shadowLeftHandIndex >= shadowDots.Length || shadowRightHandIndex >= shadowDots.Length) return;

        Vector3 xriL = Vector3.ProjectOnPlane(xriLeft.position - xriHead.position, Vector3.up);
        Vector3 xriR = Vector3.ProjectOnPlane(xriRight.position - xriHead.position, Vector3.up);

        Vector3 sHeadPos = shadowDots[headIndex].localPosition;
        Vector3 sLPos = shadowDots[shadowLeftHandIndex].localPosition;
        Vector3 sRPos = shadowDots[shadowRightHandIndex].localPosition;

        Vector3 shadowL = Vector3.ProjectOnPlane(sLPos - sHeadPos, Vector3.up);
        Vector3 shadowR = Vector3.ProjectOnPlane(sRPos - sHeadPos, Vector3.up);

        float crossSum = (shadowL.z * xriL.x - shadowL.x * xriL.z) + (shadowR.z * xriR.x - shadowR.x * xriR.z);
        float dotSum = (shadowL.x * xriL.x + shadowL.z * xriL.z) + (shadowR.x * xriR.x + shadowR.z * xriR.z);

        yawCorrection = Mathf.Atan2(crossSum, dotSum) * Mathf.Rad2Deg;
        if (yawCorrection < 0) yawCorrection += 360f;
    }

    public void ApplyShadowRotation()
    {
        if (continuousAutoAlign) CalculateAutoAlignYaw();
        if (shadowRoot != null && xriHead != null)
        {
            shadowRoot.rotation = Quaternion.Euler(0, yawCorrection, 0);
        }
    }

    private void AnchorShadowPosition()
    {
        if (xriHead == null || shadowDots == null) return;
        Transform shadowHead = shadowDots[headIndex];
        if (shadowHead == null) return;

        // Where the head SHOULD be
        Vector3 targetPos = xriHead.position - (xriHead.rotation * shadowHeadToSkullOffset);
        // Where the head IS (in world space)
        Vector3 currentHeadWorld = shadowHead.position;
        // Move root by the difference
        shadowRoot.position += (targetPos - currentHeadWorld);
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

    private void UpdateLabels()
    {
        if (dotLabels == null) return;
        for (int i = 0; i < dotLabels.Length; i++)
        {
            if (dotLabels[i] == null) continue;
            if (dotLabels[i].gameObject.activeSelf != showLabels) dotLabels[i].gameObject.SetActive(showLabels);
            if (showLabels)
            {
                dotLabels[i].transform.position = shadowDots[i].position + Vector3.up * 0.1f;
                dotLabels[i].transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward);
            }
        }
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

    // ===================================================================================
    //  STREAMING ARCHITECTURE (Indexed)
    // ===================================================================================

    public interface IFrame
    {
        long Ticks { get; set; }
        float Time { get; set; }
    }

    public class XriFrame : IFrame
    {
        public long Ticks { get; set; }
        public float Time { get; set; }

        // Metadata
        public byte State;
        public int TrialNum;
        public float Stimulus;
        public Vector3 SpawnPos;
        public Vector3 GoalPos;

        // Poses
        public Vector3 HeadPos, LPos, RPos;
        public Quaternion HeadRot, LRot, RRot;
        public Vector3 GazeOrigin, GazeDirection;

        public void CopyFrom(XriFrame o)
        {
            Ticks = o.Ticks; Time = o.Time;

            State = o.State; TrialNum = o.TrialNum;
            Stimulus = o.Stimulus; SpawnPos = o.SpawnPos; GoalPos = o.GoalPos;

            HeadPos = o.HeadPos; LPos = o.LPos; RPos = o.RPos;
            HeadRot = o.HeadRot; LRot = o.LRot; RRot = o.RRot;
            GazeOrigin = o.GazeOrigin; GazeDirection = o.GazeDirection;
        }
    }

    public class ShadowFrame : IFrame
    {
        public long Ticks { get; set; }
        public float Time { get; set; }
        public Vector3[] Positions = new Vector3[17];

        public void CopyFrom(ShadowFrame o)
        {
            Ticks = o.Ticks; Time = o.Time;
            Array.Copy(o.Positions, Positions, 17);
        }
    }

    // --- Base Indexed Streamer ---
    public abstract class IndexedStreamer<T> : IDisposable where T : class, IFrame, new()
    {
        protected FileStream fs;
        protected BinaryReader br;

        // RAM Indices (Lightweight)
        protected List<long> fileOffsets = new List<long>(); // Where does the frame start?
        protected List<float> timeStamps = new List<float>(); // What time is it?

        // Timing
        public long StartTick { get; protected set; }
        public float Duration { get; protected set; }
        public int FrameCount => fileOffsets.Count;

        protected long globalStartTick;
        protected double frequency;

        // Playback Buffers
        protected T frameA = new T();
        protected T frameB = new T();
        private int lastReadIndex = -1;

        public IndexedStreamer(string path)
        {
            fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192);
            br = new BinaryReader(fs);

            ReadHeader();
            BuildIndex(); // Scan file once
        }

        protected abstract void ReadHeader();
        protected abstract void ReadFramePayload(T target); // Read actual data
        protected abstract void SkipFramePayload(); // Just move pointer forward

        // The "Scan"
        private void BuildIndex()
        {
            long len = fs.Length;
            // Assumes file pointer is at start of first frame data
            while (fs.Position < len)
            {
                long pos = fs.Position;
                long ticks = br.ReadInt64(); // Every frame starts with Ticks

                if (fileOffsets.Count == 0) StartTick = ticks;

                fileOffsets.Add(pos);
                // Store raw duration relative to self (will normalize later)
                // We don't have frequency yet, so just store ticks.
                // NOTE: We'll overwrite 'timeStamps' once we set timing.
                timeStamps.Add(0); // Placeholder

                // Reset to start of payload (after ticks) or just continue?
                // Standard convention: ReadTicks advanced 8 bytes.
                // We need to skip the REST of the frame.
                SkipFramePayload();
            }
        }

        public void SetTiming(long globalStart, long freq)
        {
            globalStartTick = globalStart;
            frequency = (double)freq;

            // Retroactively calculate seconds for the index
            // We have to re-read ticks or just assume we can't without re-seeking?
            // Actually, to keep memory low, we shouldn't store ticks in RAM.
            // But we need to know the time of the frames for BinarySearch.

            // Re-pass: Update timestamps in the list.
            // This is fast (RAM only) if we stored ticks, but we didn't store ticks to save RAM.
            // Compromise: We need to re-read the ticks from disk? No, that's slow.
            // Better approach: Store Ticks in the BuildIndex phase temporarily, then convert.

            // For now, let's just re-read the ticks from the start, it's safer.
            for (int i = 0; i < fileOffsets.Count; i++)
            {
                fs.Seek(fileOffsets[i], SeekOrigin.Begin);
                long t = br.ReadInt64();
                timeStamps[i] = (float)((t - globalStartTick) / frequency);
            }

            if (timeStamps.Count > 0)
                Duration = timeStamps[timeStamps.Count - 1];
        }

        public (T, T, float) GetFrame(float time)
        {
            if (fileOffsets.Count == 0) return (frameA, frameA, 0);

            // Find the index for 'time'
            int index = timeStamps.BinarySearch(time);
            if (index < 0) index = ~index - 1;
            if (index < 0) index = 0;
            if (index >= fileOffsets.Count - 1) index = fileOffsets.Count - 2;
            if (index < 0) index = 0; // fallback if only 1 frame

            // Do we need to read from disk?
            // If we are already at this index, don't re-read
            if (index != lastReadIndex)
            {
                ReadAtIndex(index, frameA);
                ReadAtIndex(index + 1, frameB);
                lastReadIndex = index;
            }

            // Interpolate
            float t = 0f;
            float duration = frameB.Time - frameA.Time;
            if (duration > 1e-5f)
                t = (time - frameA.Time) / duration;

            t = Mathf.Clamp01(t);
            return (frameA, frameB, t);
        }

        private void ReadAtIndex(int index, T target)
        {
            if (index < 0 || index >= fileOffsets.Count) return;

            fs.Seek(fileOffsets[index], SeekOrigin.Begin);

            // We must read ticks again to set the object state, 
            // even though we have it in the index
            target.Ticks = br.ReadInt64();
            target.Time = timeStamps[index];

            ReadFramePayload(target);
        }

        public void Dispose()
        {
            br?.Close();
            fs?.Close();
        }
    }

    public class XriStreamer : IndexedStreamer<XriFrame>
    {
        private bool hasState;
        public XriStreamer(string path) : base(path) { }

        protected override void ReadHeader()
        {
            int magic = br.ReadInt32();
            int version = br.ReadInt32();
            int count = br.ReadInt32();
            byte flags = br.ReadByte();

            Debug.Log($"[XRI Header] magic=0x{magic:X8} version={version} count={count} flags=0x{flags:X2}");

            if (magic != XriBinMagic)
                throw new InvalidDataException($"Bad XRI magic. Expected 0x{XriBinMagic:X8}, got 0x{magic:X8}");
            if (version != XriBinVersion)
                Debug.LogError($"XRI version mismatch. Expected {XriBinVersion}, got {version}");

            hasState = (flags & 1) != 0;
        }

        protected override void SkipFramePayload()
        {
            // Ticks already read by Base.
            if (hasState) fs.Seek(1, SeekOrigin.Current); // Skip State

            // Skip Metadata
            // TrialNum(4) + Stimulus(4) + Spawn(12) + Goal(12) = 32 bytes
            fs.Seek(32, SeekOrigin.Current);

            // Skip Devices
            for (int i = 0; i < 4; i++)
            {
                byte id = br.ReadByte();
                fs.Seek(12, SeekOrigin.Current); // Skip Vector3

                if (id == 3) // DevGaze
                    fs.Seek(12, SeekOrigin.Current); // Gaze Dir
                else
                    fs.Seek(16, SeekOrigin.Current); // Quaternion
            }
        }

        protected override void ReadFramePayload(XriFrame f)
        {
            f.State = hasState ? br.ReadByte() : (byte)0;

            long posBeforeTrial = fs.Position;
            byte[] trialBytes = br.ReadBytes(4);
            int trialAsInt = BitConverter.ToInt32(trialBytes, 0);

            // Now read stimulus as your code would
            float stim = br.ReadSingle();

            Debug.Log($"[XRI TrialBytes] pos=0x{posBeforeTrial:X} bytes={trialBytes[0]:X2} {trialBytes[1]:X2} {trialBytes[2]:X2} {trialBytes[3]:X2} trial={trialAsInt} stim={stim}");

            // Rewind and continue normal parsing exactly as before
            fs.Position = posBeforeTrial;
            f.TrialNum = br.ReadInt32();
            f.Stimulus = br.ReadSingle();
            f.SpawnPos = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            f.GoalPos = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

            for (int i = 0; i < 4; i++)
            {
                byte id = br.ReadByte();
                Vector3 v = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

                if (id == 3) // DevGaze
                {
                    f.GazeOrigin = v;
                    f.GazeDirection = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                }
                else
                {
                    Quaternion q = new Quaternion(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    if (id == 0) { f.HeadPos = v; f.HeadRot = q; }
                    else if (id == 1) { f.LPos = v; f.LRot = q; }
                    else if (id == 2) { f.RPos = v; f.RRot = q; }
                }
            }
        }
    }

    // --- Shadow Implementation ---
    public class ShadowStreamer : IndexedStreamer<ShadowFrame>
    {
        public long Frequency { get; private set; }
        public ShadowStreamer(string path) : base(path) { }

        protected override void ReadHeader()
        {
            br.ReadInt32();
            br.ReadInt32();
            br.ReadInt32();
            Frequency = br.ReadInt64();
            fs.Seek(80, SeekOrigin.Current); // Padding
        }

        protected override void SkipFramePayload()
        {
            // Ticks already read.
            // Shadow frame body: Skip 8 bytes + 17 bones
            // Each bone: Vector3(12) + Skip(16) = 28 bytes
            // Total: 8 + (17 * 28) = 484 bytes
            fs.Seek(8 + (17 * 28), SeekOrigin.Current);
        }

        protected override void ReadFramePayload(ShadowFrame f)
        {
            fs.Seek(8, SeekOrigin.Current); // Skip 8
            for (int b = 0; b < 17; b++)
            {
                f.Positions[b].x = br.ReadSingle();
                f.Positions[b].y = br.ReadSingle();
                f.Positions[b].z = br.ReadSingle();
                fs.Seek(16, SeekOrigin.Current); // Skip 4 floats
            }
        }
    }
}