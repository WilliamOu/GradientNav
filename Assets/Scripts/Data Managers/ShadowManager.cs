using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Motion.SDK;

// This code uses the Motion.SDK, distributed under a separate license which can be found under:
//   Motion SDK/LICENSE
// The Motion SDK repository can be found at:
//   https://github.com/motion-workshop/sdk
//
// PURPOSE
// - Connect to Shadow Motion Service (Configurable stream) via Motion.SDK.Client
// - Request channels via XML (we request <Lq/> and <c/>)
// - Parse incoming frames via Motion.SDK.Format.Configurable
// - Log 17 segment poses (position + quaternion rotation) to a fixed-layout binary file
//
// TIMING AND SYNC
// - Shadow data arrives asynchronously.
// - We timestamp each received frame with Stopwatch ticks at ingestion (ArrivalStopwatchTicks).
// - We also store UnityArrivalSeconds mapped from those ticks via LogManager.EstimateUnityTimeFromStopwatchTicks.
//   This assumes LogManager updates its mapping frequently on the main thread.
//   If you later decide you want maximum decoupling, you can remove UnityArrivalSeconds from the file and derive it offline.
public sealed class ShadowManager : MonoBehaviour
{
    [Header("Motion Service")]
    public string host = "127.0.0.1";
    public int configurablePort = 32076;

    [Header("Skeleton")]
    public const int BoneCount = 17;

    [Header("Logging")]
    public int writeBufferBytes = 1 << 20; // 1 MB
    public int queueCapacity = 4096;

    [Header("Diagnostics")]
    public bool isRunning;
    public bool isConnected;
    public long framesWritten;
    public long framesDropped;
    public string status = "Idle";

    private CancellationTokenSource cts;
    private Task ingestTask;
    private Task writerTask;

    private FileStream fileStream;
    private readonly object fileLock = new object();

    private BoundedQueue<ShadowFrame> queue;

    private int[] nodeIds;
    private bool nodeMapWritten;
    private long headerNodeIdsOffset = -1;
    private long _headerMappedCountOffset = -1;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ShadowFrame
    {
        public long ArrivalStopwatchTicks;
        public double UnityArrivalSeconds;
        public fixed float BonePose[17 * 7];

        public void SetBone(int boneIndex, Vector3 pos, Quaternion rot)
        {
            if ((uint)boneIndex >= 17u) return;
            int o = boneIndex * 7;
            unsafe
            {
                fixed (float* p = BonePose)
                {
                    p[o + 0] = pos.x; p[o + 1] = pos.y; p[o + 2] = pos.z;
                    p[o + 3] = rot.x; p[o + 4] = rot.y; p[o + 5] = rot.z; p[o + 6] = rot.w;
                }
            }
        }
    }

    public void BeginLogging()
    {
        if (isRunning) return;

        string participantFolder =
        AppManager.Instance.Session.GetParticipantFolderPath();

        string sessionFolderName =
            AppManager.Instance.Session.GetFileName();

        string sessionFolderPath =
            Path.Combine(participantFolder, sessionFolderName);

        Directory.CreateDirectory(sessionFolderPath);

        string fileName = sessionFolderName + "_Shadow.bin";
        string path = Path.Combine(sessionFolderPath, fileName);

        try
        {
            isRunning = true;
            isConnected = false;
            nodeIds = null;
            nodeMapWritten = false;
            headerNodeIdsOffset = -1;
            _headerMappedCountOffset = -1;
            framesWritten = 0;
            framesDropped = 0;

            fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, writeBufferBytes, false);
            WriteHeader(fileStream);

            queue = new BoundedQueue<ShadowFrame>(queueCapacity);
            cts = new CancellationTokenSource();

            writerTask = Task.Factory.StartNew(() => WriterLoop(cts.Token), TaskCreationOptions.LongRunning);
            ingestTask = Task.Factory.StartNew(() => IngestLoop(cts.Token), TaskCreationOptions.LongRunning);

            status = $"Logging: {path}";
            Debug.Log($"[ShadowManager] Started. Writing to: {path}");
        }
        catch (Exception e)
        {
            status = $"Start failed (ignore if the shadow system is not connected): {e.Message}";
            Debug.Log($"[ShadowManager] {status}");
            SafeStopInternal();
        }
    }

    public void EndLogging()
    {
        if (!isRunning) return;
        SafeStopInternal();
        Debug.Log("[ShadowManager] Stopped.");
    }

    public void ManualUpdate() { }

    private void OnDestroy() => EndLogging();

    private void IngestLoop(CancellationToken ct)
    {
        status = "Connecting";
        // SDK Client is not IDisposable
        var client = new Motion.SDK.Client(host, configurablePort);

        try
        {
            if (!client.isConnected())
                throw new Exception("Could not connect to Shadow Configurable service");

            isConnected = true;

            // XML Request: Local Quats (Lq) and Positions (c)
            string xmlCmd = "<?xml version=\"1.0\"?><configurable><Lq/><lt/></configurable>";
            byte[] xmlBytes = System.Text.Encoding.ASCII.GetBytes(xmlCmd);
            client.writeData(xmlBytes);

            status = "Streaming";

            while (!ct.IsCancellationRequested)
            {
                byte[] rawData = client.readData(); // Blocks ~1s

                if (ct.IsCancellationRequested) break;
                if (rawData == null || rawData.Length == 0) continue;

                var container = Motion.SDK.Format.Configurable(rawData);
                if (container == null || container.Count == 0) continue;

                EnsureNodeMapping(container);

                // CRITICAL FIX: If mapping failed (e.g. node count mismatch),
                // nodeIds is null. We must skip this frame to avoid crashing.
                if (nodeIds == null)
                {
                    // Throttle error logs if you want, or rely on status string
                    continue;
                }

                long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();

                ShadowFrame frame = new ShadowFrame
                {
                    ArrivalStopwatchTicks = nowTicks,
                    UnityArrivalSeconds = LogManager.EstimateUnityTimeFromStopwatchTicks(nowTicks)
                };

                for (int bi = 0; bi < BoneCount; bi++)
                {
                    // Safety check against array bounds
                    if (bi >= nodeIds.Length) break;

                    int nodeId = nodeIds[bi];
                    if (nodeId < 0) continue;

                    if (container.TryGetValue(nodeId, out var element))
                    {
                        if (element.size() >= 7)
                        {
                            // SDK: [w, x, y, z] -> Unity: (x, y, z, w)
                            Quaternion rot = new Quaternion(
                                element.value(1), element.value(2), element.value(3), element.value(0)
                            );

                            // SDK: [x, y, z]
                            Vector3 pos = new Vector3(
                                element.value(4), element.value(5), element.value(6)
                            );

                            frame.SetBone(bi, pos, rot);
                        }
                    }
                }

                if (!queue.TryEnqueue(frame))
                    Interlocked.Increment(ref framesDropped);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (!ct.IsCancellationRequested)
            {
                status = $"Ingest error: {e.Message}";
                Debug.LogError($"[ShadowManager] {status}");
            }
        }
        finally
        {
            isConnected = false;
            try { client.close(); } catch { }
        }
    }

    private void EnsureNodeMapping(IDictionary<int, Motion.SDK.Format.ConfigurableElement> container)
    {
        if (nodeIds != null && nodeMapWritten) return;

        if (nodeIds == null)
        {
            var keys = new List<int>(BoneCount);
            foreach (var kvp in container)
            {
                if (kvp.Value != null && kvp.Value.size() >= 7)
                    keys.Add(kvp.Key);
            }

            keys.Sort();

            if (keys.Count == 0)
            {
                status = "Waiting for nodes: found 0";
                return;
            }

            int n = Math.Min(BoneCount, keys.Count);

            nodeIds = new int[BoneCount];
            for (int i = 0; i < BoneCount; i++)
                nodeIds[i] = (i < n) ? keys[i] : -1;

            Debug.Log($"[ShadowManager] Mapped {n}/{BoneCount} nodes: {string.Join(",", nodeIds)}");
        }

        if (!nodeMapWritten)
        {
            TryWriteNodeIdsToHeader(nodeIds);
            nodeMapWritten = true;
            status = "Streaming (Active)";
        }
    }

    private void WriteHeader(FileStream fs)
    {
        using var bw = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: true);
        bw.Write(0x53484457);
        bw.Write(3);
        bw.Write(BoneCount);
        bw.Write((long)System.Diagnostics.Stopwatch.Frequency);
        bw.Write(BoneCount); // nodeIdCount

        long mappedCountOffset = fs.Position;
        bw.Write(0); // mappedCount placeholder

        headerNodeIdsOffset = fs.Position;
        for (int i = 0; i < BoneCount; i++) bw.Write(-1);
        bw.Write(0);
        bw.Flush();

        _headerMappedCountOffset = mappedCountOffset;
    }

    private void TryWriteNodeIdsToHeader(int[] ids)
    {
        if (ids == null || ids.Length != BoneCount) return;
        lock (fileLock)
        {
            if (fileStream == null || headerNodeIdsOffset < 0) return;
            if (_headerMappedCountOffset < 0) return;
            long prevPos = fileStream.Position;
            try
            {
                using var bw = new BinaryWriter(fileStream, System.Text.Encoding.UTF8, leaveOpen: true);
                int mapped = 0;
                for (int i = 0; i < BoneCount; i++) if (ids[i] >= 0) mapped++;

                fileStream.Position = _headerMappedCountOffset;
                bw.Write(mapped);

                fileStream.Position = headerNodeIdsOffset;
                for (int i = 0; i < BoneCount; i++) bw.Write(ids[i]);
                bw.Flush();
            }
            finally
            {
                fileStream.Position = prevPos;
            }
        }
    }

    private void WriterLoop(CancellationToken ct)
    {
        try
        {
            unsafe
            {
                while (!ct.IsCancellationRequested)
                {
                    if (queue == null) break;
                    if (!queue.TryDequeue(out ShadowFrame frame, 50)) continue;

                    var span = new ReadOnlySpan<byte>(&frame, sizeof(ShadowFrame));
                    lock (fileLock) { fileStream?.Write(span); }
                    Interlocked.Increment(ref framesWritten);
                }

                // Drain
                if (queue != null)
                {
                    while (queue.TryDequeue(out ShadowFrame frame))
                    {
                        var span = new ReadOnlySpan<byte>(&frame, sizeof(ShadowFrame));
                        lock (fileLock) { fileStream?.Write(span); }
                        Interlocked.Increment(ref framesWritten);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogError($"[ShadowManager] Writer error: {e.Message}");
        }
    }

    private void SafeStopInternal()
    {
        status = "Stopping";
        isRunning = false;
        try { cts?.Cancel(); } catch { }
        try { ingestTask?.Wait(1000); } catch { }
        try { writerTask?.Wait(1000); } catch { } // Wait less time to avoid freeze

        lock (fileLock)
        {
            try { fileStream?.Flush(); } catch { }
            try { fileStream?.Dispose(); } catch { }
            fileStream = null;
        }
        Cleanup();
        status = "Stopped";
    }

    private void Cleanup()
    {
        try { cts?.Dispose(); } catch { }
        cts = null;
        ingestTask = null;
        writerTask = null;
        queue = null;
        nodeIds = null;
        nodeMapWritten = false;
        headerNodeIdsOffset = -1;
        _headerMappedCountOffset = -1;
        isConnected = false;
    }

    private sealed class BoundedQueue<T> where T : struct
    {
        private readonly T[] buffer;
        private int head, tail, count;
        private readonly object gate = new object();
        public BoundedQueue(int capacity) { buffer = new T[Math.Max(64, capacity)]; }
        public bool TryEnqueue(in T item)
        {
            lock (gate)
            {
                if (count == buffer.Length) return false;
                buffer[tail] = item;
                tail = (tail + 1) % buffer.Length;
                count++;
                Monitor.Pulse(gate);
                return true;
            }
        }
        public bool TryDequeue(out T item, int timeoutMs = 0)
        {
            lock (gate)
            {
                if (count == 0)
                {
                    if (timeoutMs <= 0 || !Monitor.Wait(gate, timeoutMs))
                    {
                        item = default; return false;
                    }
                    if (count == 0) { item = default; return false; }
                }
                item = buffer[head];
                head = (head + 1) % buffer.Length;
                count--;
                return true;
            }
        }
        public bool TryDequeue(out T item) => TryDequeue(out item, 0);
    }
}