using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics; // Stopwatch

// To add a new data logging field:
// 1) Add the field to the LogFrame struct
// 2) Populate it in CaptureState()
// 3) Write it in AppendToRow() (keep order consistent)
// 4) Add the column name to the header in BeginLogging()
public class LogManager
{
    public const int MinBufferSize = 1000;
    public const int MaxBufferSize = 30000;
    private readonly string[] stateNames;

    public static readonly long StopwatchFrequency = Stopwatch.Frequency;

    // Seqlock state: even = stable, odd = write in progress.
    private static int _timeMapVersion;
    private static long _timeMapStopwatchTicks;
    private static long _timeMapUnityTimeBits;

    public static void UpdateMainThreadTimeMapping(long stopwatchTicks, double unityTimeSeconds)
    {
        // Begin write (make version odd)
        Interlocked.Increment(ref _timeMapVersion);

        // Write payload
        _timeMapStopwatchTicks = stopwatchTicks;
        _timeMapUnityTimeBits = BitConverter.DoubleToInt64Bits(unityTimeSeconds);

        // End write (make version even)
        Interlocked.Increment(ref _timeMapVersion);
    }

    public static double EstimateUnityTimeFromStopwatchTicks(long stopwatchTicks)
    {
        while (true)
        {
            int v0 = Volatile.Read(ref _timeMapVersion);
            if ((v0 & 1) != 0) continue; // writer in progress

            long sw0 = Volatile.Read(ref _timeMapStopwatchTicks);
            long tBits = Volatile.Read(ref _timeMapUnityTimeBits);

            int v1 = Volatile.Read(ref _timeMapVersion);
            if (v0 == v1 && (v1 & 1) == 0)
            {
                double t0 = BitConverter.Int64BitsToDouble(tBits);
                return t0 + (stopwatchTicks - sw0) / (double)StopwatchFrequency;
            }
            // else: mapping changed mid-read, retry
        }
    }

    public struct LogFrame
    {
        public string Event;
        public string ParticipantID;
        public float SigmaScale;
        public string MapType;
        public string State;
        public int TrialNumber;

        // Timing
        public int FrameIndex;

        // Monotonic Unity time (ignores timeScale)
        public double GlobalTime;

        // Stopwatch ticks captured on the main thread at the same moment as GlobalTime
        // (useful as a sync anchor for async systems)
        public long StopwatchTicks;

        public Vector3 HeadPos;
        public Vector3 HeadRotEuler;
        public float StimulusIntensity;
        public Vector2 SpawnPosition;
        public Vector2 GoalPosition;

        public Vector3 GazeOrigin;
        public Vector3 GazeDirection;

        public Vector3 LHandPos;
        public Vector3 LHandRotEuler;
        public Vector3 RHandPos;
        public Vector3 RHandRotEuler;

        public void AppendToRow(StringBuilder sb)
        {
            // Event (quoted + escaped)
            string evt = Event ?? "";
            if (evt.IndexOf('\"') >= 0) evt = evt.Replace("\"", "\"\"");
            sb.Append('"').Append(evt).Append('"').Append(',');

            // Quoted strings
            sb.Append('"').Append(ParticipantID ?? "").Append('"').Append(',');
            AppendFloat3(sb, SigmaScale); sb.Append(',');
            sb.Append('"').Append(MapType ?? "").Append('"').Append(',');

            sb.Append(State).Append(',');
            sb.Append(TrialNumber).Append(',');

            // Timing
            sb.Append(FrameIndex).Append(',');
            AppendDouble6(sb, GlobalTime); sb.Append(',');
            sb.Append(StopwatchTicks).Append(',');

            // Head
            AppendFloat3(sb, HeadPos.x); sb.Append(',');
            AppendFloat3(sb, HeadPos.y); sb.Append(',');
            AppendFloat3(sb, HeadPos.z); sb.Append(',');
            AppendFloat3(sb, HeadRotEuler.x); sb.Append(',');
            AppendFloat3(sb, HeadRotEuler.y); sb.Append(',');
            AppendFloat3(sb, HeadRotEuler.z); sb.Append(',');

            // Task fields
            AppendFloat3(sb, StimulusIntensity); sb.Append(',');
            AppendFloat3(sb, SpawnPosition.x); sb.Append(',');
            AppendFloat3(sb, SpawnPosition.y); sb.Append(',');
            AppendFloat3(sb, GoalPosition.x); sb.Append(',');
            AppendFloat3(sb, GoalPosition.y); sb.Append(',');

            // Gaze
            AppendFloat3(sb, GazeOrigin.x); sb.Append(',');
            AppendFloat3(sb, GazeOrigin.y); sb.Append(',');
            AppendFloat3(sb, GazeOrigin.z); sb.Append(',');
            AppendFloat3(sb, GazeDirection.x); sb.Append(',');
            AppendFloat3(sb, GazeDirection.y); sb.Append(',');
            AppendFloat3(sb, GazeDirection.z); sb.Append(',');

            // Hands
            AppendFloat3(sb, LHandPos.x); sb.Append(',');
            AppendFloat3(sb, LHandPos.y); sb.Append(',');
            AppendFloat3(sb, LHandPos.z); sb.Append(',');
            AppendFloat3(sb, LHandRotEuler.x); sb.Append(',');
            AppendFloat3(sb, LHandRotEuler.y); sb.Append(',');
            AppendFloat3(sb, LHandRotEuler.z); sb.Append(',');

            AppendFloat3(sb, RHandPos.x); sb.Append(',');
            AppendFloat3(sb, RHandPos.y); sb.Append(',');
            AppendFloat3(sb, RHandPos.z); sb.Append(',');
            AppendFloat3(sb, RHandRotEuler.x); sb.Append(',');
            AppendFloat3(sb, RHandRotEuler.y); sb.Append(',');
            AppendFloat3(sb, RHandRotEuler.z);

            sb.AppendLine();
        }
    }

    // ---------- Double-buffering ----------
    private List<LogFrame> activeBuffer;
    private readonly ConcurrentQueue<List<LogFrame>> pendingWrites = new ConcurrentQueue<List<LogFrame>>();
    private readonly ConcurrentQueue<List<LogFrame>> freeBuffers = new ConcurrentQueue<List<LogFrame>>();

    private bool isLogging = false;
    private bool paused = false;
    private string fullFilePath;

    // Stable cadence
    private double logAccumulator = 0.0;

    private int bufferLimit;

    // Background writer
    private FileStream fileStream;
    private StreamWriter streamWriter;

    private readonly AutoResetEvent writeSignal = new AutoResetEvent(false);
    private CancellationTokenSource writerCts;
    private Task writerTask;

    public LogManager()
    {
        // This automatically pulls "Training", "Trial", etc. from the enum itself.
        stateNames = Enum.GetNames(typeof(SessionDataManager.GameState));

        activeBuffer = new List<LogFrame>(MaxBufferSize);
        // Seed a free buffer so the first flush doesn’t allocate.
        freeBuffers.Enqueue(new List<LogFrame>(MaxBufferSize));
    }

    private static string CleanEventName(string eventName)
    {
        if (string.IsNullOrEmpty(eventName)) return "";
        return eventName.Replace("\n", " ").Replace("\r", " ");
    }

    private LogFrame CaptureState(string eventName = "")
    {
        var session = AppManager.Instance.Session;
        var player = AppManager.Instance.Player;
        var settings = AppManager.Instance.Settings;

        Transform cameraT = player.CameraPosition();
        Vector3 headPos = cameraT != null ? cameraT.position : Vector3.zero;
        Vector3 headRot = cameraT != null ? cameraT.eulerAngles : Vector3.zero;

        Vector3 lPos, lRot, rPos, rRot;
        Vector3 gazeOrig, gazeDir;

        if (session.IsVRMode)
        {
            player.GetVRHandWorldData(out lPos, out lRot, out rPos, out rRot);
            player.GetVRGazeWorldData(out gazeOrig, out gazeDir);
        }
        else
        {
            lPos = lRot = rPos = rRot = Vector3.zero;
            if (cameraT != null)
            {
                gazeOrig = cameraT.position;
                gazeDir = cameraT.forward;
            }
            else
            {
                gazeOrig = Vector3.zero;
                gazeDir = Vector3.forward;
            }
        }

        // Capture both clocks on the main thread
        long sw = Stopwatch.GetTimestamp();
        double t = Time.realtimeSinceStartupAsDouble;

        // Update the public mapping for async systems (mocap manager will use it)
        UpdateMainThreadTimeMapping(sw, t);

        int stateIndex = (int)session.State;

        return new LogFrame
        {
            Event = CleanEventName(eventName),
            ParticipantID = session.ParticipantId,
            SigmaScale = settings.SigmaScale,
            MapType = session.MapType,
            State = (stateIndex >= 0 && stateIndex < stateNames.Length)
            ? stateNames[stateIndex]
            : session.State.ToString(),
            TrialNumber = session.TrialNumber,

            FrameIndex = Time.frameCount,
            GlobalTime = t,
            StopwatchTicks = sw,

            HeadPos = headPos,
            HeadRotEuler = headRot,
            StimulusIntensity = session.State == SessionDataManager.GameState.Trial ? player.StimulusIntensity : -1f,
            SpawnPosition = session.SpawnPosition,
            GoalPosition = session.GoalPosition,

            GazeOrigin = gazeOrig,
            GazeDirection = gazeDir,

            LHandPos = lPos,
            LHandRotEuler = lRot,
            RHandPos = rPos,
            RHandRotEuler = rRot
        };
    }

    public void BeginLogging()
    {
        if (isLogging) return;

        string participantFolder =
        AppManager.Instance.Session.GetParticipantFolderPath();

        string sessionFolderName =
            AppManager.Instance.Session.GetFileName();

        string sessionFolderPath =
            Path.Combine(participantFolder, sessionFolderName);

        Directory.CreateDirectory(sessionFolderPath);

        string fileName = sessionFolderName + "_XRI.csv";
        fullFilePath = Path.Combine(sessionFolderPath, fileName);

        // Header adds FrameIndex + GlobalTime + StopwatchTicks
        // Note: SpawnXZ and GoalXZ are stored as a Vector2 which represents the pair as an x, y coordinate
        string header =
            "Event,ParticipantID,SigmaScale,MapType,State,TrialNumber,FrameIndex,GlobalTime,StopwatchTicks," +
            "HeadX,HeadY,HeadZ,RotX,RotY,RotZ,StimulusIntensity,SpawnX,SpawnZ,GoalX,GoalZ," +
            "GazeOriginX,GazeOriginY,GazeOriginZ,GazeDirX,GazeDirY,GazeDirZ," +
            "LHandX,LHandY,LHandZ,LRotX,LRotY,LRotZ,RHandX,RHandY,RHandZ,RRotX,RRotY,RRotZ\n";

        fileStream = new FileStream(
            fullFilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 1 << 20,
            useAsync: false);

        streamWriter = new StreamWriter(fileStream, new UTF8Encoding(false), bufferSize: 1 << 16);
        streamWriter.NewLine = "\n";
        streamWriter.Write(header);
        streamWriter.Flush();

        activeBuffer.Clear();
        logAccumulator = 0.0;

        int settingValue = AppManager.Instance.Settings.BufferSizeBeforeWrite;
        bufferLimit = Mathf.Clamp(settingValue, MinBufferSize, MaxBufferSize);

        paused = false;
        isLogging = true;

        writerCts = new CancellationTokenSource();
        writerTask = Task.Run(() => WriterLoop(writerCts.Token));

        UnityEngine.Debug.Log($"[LogManager] Recording to: {fullFilePath}");
    }

    public void EndLogging()
    {
        if (!isLogging) return;
        isLogging = false;

        if (activeBuffer.Count > 0) EnqueueFlush();

        try
        {
            writerCts?.Cancel();
            writeSignal.Set();
            writerTask?.Wait();
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[LogManager] Writer shutdown failed: {e.Message}");
        }

        try
        {
            streamWriter?.Flush();
            streamWriter?.Dispose();
            fileStream?.Dispose();
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[LogManager] Stream dispose failed: {e.Message}");
        }

        writerCts?.Dispose();
        writerCts = null;
        writerTask = null;

        UnityEngine.Debug.Log("[LogManager] Recording stopped.");
    }

    public void PauseLogging() { if (isLogging) paused = true; }
    public void ResumeLogging() { if (isLogging) paused = false; }

    public void ManualUpdate()
    {
        if (!isLogging || paused) return;

        logAccumulator += Time.unscaledDeltaTime;

        double interval = AppManager.Instance.Settings.DataLogInterval;
        if (interval <= 0.0) interval = 1.0 / 90.0;

        int maxCatchUp = 25;
        int wrote = 0;

        while (logAccumulator >= interval && wrote < maxCatchUp)
        {
            logAccumulator -= interval;
            activeBuffer.Add(CaptureState("")); // hot path
            wrote++;
        }

        if (activeBuffer.Count >= bufferLimit)
            EnqueueFlush();
    }

    public void LogEvent(string input)
    {
        if (!isLogging) return;

        activeBuffer.Add(CaptureState(input ?? ""));
        if (activeBuffer.Count >= bufferLimit)
            EnqueueFlush();
    }

    private void EnqueueFlush()
    {
        if (activeBuffer.Count == 0) return;

        // Get a new buffer for active logging
        if (!freeBuffers.TryDequeue(out var newActive))
            newActive = new List<LogFrame>(MaxBufferSize);

        // Move current active into pending queue
        var toWrite = activeBuffer;
        activeBuffer = newActive;
        activeBuffer.Clear();

        pendingWrites.Enqueue(toWrite);
        writeSignal.Set();
    }

    private void WriterLoop(CancellationToken ct)
    {
        var sb = new StringBuilder(1 << 20);
        var scratch = new char[64 * 1024];

        while (!ct.IsCancellationRequested)
        {
            writeSignal.WaitOne(50);

            while (pendingWrites.TryDequeue(out var buffer))
            {
                try
                {
                    sb.Clear();
                    for (int i = 0; i < buffer.Count; i++)
                        buffer[i].AppendToRow(sb);

                    WriteStringBuilder(streamWriter, sb, scratch);

                    // Consider NOT flushing every batch. Flush periodically or at end.
                    // streamWriter.Flush();

                    buffer.Clear();
                    freeBuffers.Enqueue(buffer);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"[LogManager] Writer loop failed: {e.Message}");
                    buffer.Clear();
                    freeBuffers.Enqueue(buffer);
                }
            }
        }

        // Drain remaining buffers on shutdown
        while (pendingWrites.TryDequeue(out var buffer))
        {
            try
            {
                sb.Clear();
                for (int i = 0; i < buffer.Count; i++)
                    buffer[i].AppendToRow(sb);

                WriteStringBuilder(streamWriter, sb, scratch);
                streamWriter.Flush();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[LogManager] Final background write failed: {e.Message}");
            }
            finally
            {
                buffer.Clear();
                freeBuffers.Enqueue(buffer);
            }
        }
    }

    private static void WriteStringBuilder(StreamWriter writer, StringBuilder sb, char[] scratch)
    {
        int total = sb.Length;
        int offset = 0;

        while (offset < total)
        {
            int count = Math.Min(scratch.Length, total - offset);
            sb.CopyTo(offset, scratch, 0, count);
            writer.Write(scratch, 0, count);
            offset += count;
        }
    }

    // ---------- Allocation-free numeric formatting helpers ----------
    // These avoid per-value string allocations from ToString("F3")/ToString("F6").
    // They always use '.' as the decimal separator (invariant).

    private static void AppendFloat3(StringBuilder sb, float value)
    {
        if (float.IsNaN(value)) { sb.Append("NaN"); return; }
        if (float.IsPositiveInfinity(value)) { sb.Append("Infinity"); return; }
        if (float.IsNegativeInfinity(value)) { sb.Append("-Infinity"); return; }

        // Handle sign
        if (value < 0f) { sb.Append('-'); value = -value; }

        int whole = (int)value;
        float fracF = (value - whole) * 1000f;

        // Round to nearest int
        int frac = (int)(fracF + 0.5f);

        // Handle carry (e.g., 1.9996 -> 2.000)
        if (frac >= 1000)
        {
            whole += 1;
            frac -= 1000;
        }

        sb.Append(whole);
        sb.Append('.');

        // 3 digits, zero-padded
        int d1 = frac / 100;
        int d2 = (frac / 10) % 10;
        int d3 = frac % 10;
        sb.Append((char)('0' + d1));
        sb.Append((char)('0' + d2));
        sb.Append((char)('0' + d3));
    }

    private static void AppendDouble6(StringBuilder sb, double value)
    {
        if (double.IsNaN(value)) { sb.Append("NaN"); return; }
        if (double.IsPositiveInfinity(value)) { sb.Append("Infinity"); return; }
        if (double.IsNegativeInfinity(value)) { sb.Append("-Infinity"); return; }

        if (value < 0.0) { sb.Append('-'); value = -value; }

        long whole = (long)value;
        double fracD = (value - whole) * 1000000.0;

        long frac = (long)(fracD + 0.5);

        if (frac >= 1000000)
        {
            whole += 1;
            frac -= 1000000;
        }

        sb.Append(whole);
        sb.Append('.');

        // 6 digits, zero-padded
        // Write from highest place to lowest
        long div = 100000;
        for (int i = 0; i < 6; i++)
        {
            long digit = frac / div;
            sb.Append((char)('0' + (int)digit));
            frac -= digit * div;
            div /= 10;
        }
    }
}
