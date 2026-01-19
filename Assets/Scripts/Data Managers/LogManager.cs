using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System;

// To add a new data logging field:
// 1) Add the field to the LogFrame struct
// 2) Populate it in CaptureState()
// 3) Write it in AppendToRow() (keep order consistent)
// 4) Add the column name to the header in BeginLogging()
public class LogManager
{
    public const int MinBufferSize = 1000;
    public const int MaxBufferSize = 30000;

    public struct LogFrame
    {
        public string Event;
        public string ParticipantID;
        public float SigmaScale;
        public string MapType;
        public SessionDataManager.GameState State;
        public int TrialNumber;
        public float GlobalTime;
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
            string evt = Event ?? "";
            evt = evt.Replace("\"", "\"\"");
            sb.Append("\"").Append(evt).Append("\"").Append(",");

            sb.Append("\"").Append(ParticipantID).Append("\"").Append(",");
            sb.Append(SigmaScale).Append(",");
            sb.Append("\"").Append(MapType).Append("\"").Append(",");
            sb.Append(State.ToString()).Append(",");
            sb.Append(TrialNumber).Append(",");
            sb.Append(GlobalTime.ToString("F3")).Append(",");

            // Head Vectors
            sb.Append(HeadPos.x.ToString("F3")).Append(",");
            sb.Append(HeadPos.y.ToString("F3")).Append(",");
            sb.Append(HeadPos.z.ToString("F3")).Append(",");
            sb.Append(HeadRotEuler.x.ToString("F3")).Append(",");
            sb.Append(HeadRotEuler.y.ToString("F3")).Append(",");
            sb.Append(HeadRotEuler.z.ToString("F3")).Append(",");

            sb.Append(StimulusIntensity.ToString("F3")).Append(",");
            sb.Append(SpawnPosition.x.ToString("F3")).Append(",");
            sb.Append(SpawnPosition.y.ToString("F3")).Append(",");
            sb.Append(GoalPosition.x.ToString("F3")).Append(",");
            sb.Append(GoalPosition.y.ToString("F3")).Append(",");

            // --- Gaze Vectors (NEW) ---
            sb.Append(GazeOrigin.x.ToString("F3")).Append(",");
            sb.Append(GazeOrigin.y.ToString("F3")).Append(",");
            sb.Append(GazeOrigin.z.ToString("F3")).Append(",");
            sb.Append(GazeDirection.x.ToString("F3")).Append(",");
            sb.Append(GazeDirection.y.ToString("F3")).Append(",");
            sb.Append(GazeDirection.z.ToString("F3")).Append(",");

            // --- Hand Vectors ---
            sb.Append(LHandPos.x.ToString("F3")).Append(",");
            sb.Append(LHandPos.y.ToString("F3")).Append(",");
            sb.Append(LHandPos.z.ToString("F3")).Append(",");
            sb.Append(LHandRotEuler.x.ToString("F3")).Append(",");
            sb.Append(LHandRotEuler.y.ToString("F3")).Append(",");
            sb.Append(LHandRotEuler.z.ToString("F3")).Append(",");

            sb.Append(RHandPos.x.ToString("F3")).Append(",");
            sb.Append(RHandPos.y.ToString("F3")).Append(",");
            sb.Append(RHandPos.z.ToString("F3")).Append(",");
            sb.Append(RHandRotEuler.x.ToString("F3")).Append(",");
            sb.Append(RHandRotEuler.y.ToString("F3")).Append(",");
            sb.Append(RHandRotEuler.z.ToString("F3")); // No comma on last one

            sb.AppendLine();
        }
    }

    private List<LogFrame> activeBuffer;
    private bool isLogging = false;
    private bool paused = false;
    private string fullFilePath;
    private float logTimer;
    private int bufferLimit;
    private readonly object fileWriteLock = new object();
    private Task lastWriteTask = Task.CompletedTask;

    public LogManager()
    {
        activeBuffer = new List<LogFrame>(MaxBufferSize);
    }

    private LogFrame CaptureState(string eventName = "")
    {
        var session = AppManager.Instance.Session;
        var player = AppManager.Instance.Player;
        var settings = AppManager.Instance.Settings;

        // --- Head Data ---
        Transform cameraT = player.CameraPosition();
        Vector3 headPos = cameraT != null ? cameraT.position : Vector3.zero;
        Vector3 headRot = cameraT != null ? cameraT.eulerAngles : Vector3.zero;

        // --- Hand & Gaze Data ---
        Vector3 lPos, lRot, rPos, rRot;
        Vector3 gazeOrig, gazeDir;

        if (session.IsVRMode)
        {
            player.GetVRHandWorldData(out lPos, out lRot, out rPos, out rRot);
            player.GetVRGazeWorldData(out gazeOrig, out gazeDir); // <--- Call new method
        }
        else
        {
            lPos = lRot = rPos = rRot = Vector3.zero;
            // Desktop fallback: Gaze = Head Position + Head Forward
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

        // Clean event name
        if (!string.IsNullOrEmpty(eventName))
        {
            eventName = eventName.Replace("\n", " ").Replace("\r", " ").Replace(",", ";");
        }

        return new LogFrame
        {
            Event = eventName,
            ParticipantID = session.ParticipantId,
            SigmaScale = settings.SigmaScale,
            MapType = session.MapType,
            State = session.State,
            TrialNumber = session.TrialNumber,
            GlobalTime = Time.time,

            HeadPos = headPos,
            HeadRotEuler = headRot,
            StimulusIntensity = session.State == SessionDataManager.GameState.Trial ? player.StimulusIntensity : -1,
            SpawnPosition = session.SpawnPosition,
            GoalPosition = session.GoalPosition,

            // New Gaze Fields
            GazeOrigin = gazeOrig,
            GazeDirection = gazeDir,

            // Hand Fields
            LHandPos = lPos,
            LHandRotEuler = lRot,
            RHandPos = rPos,
            RHandRotEuler = rRot
        };
    }

    public void BeginLogging()
    {
        if (isLogging) return;

        string folder = AppManager.Instance.Session.GetParticipantFolderPath();
        string fileName = AppManager.Instance.Session.GetCSVName();
        fullFilePath = Path.Combine(folder, fileName);

        // Updated Header with Gaze columns
        string header = "Event,ParticipantID,SigmaScale,MapType,State,TrialNumber,GlobalTime,HeadX,HeadY,HeadZ,RotX,RotY,RotZ,StimulusIntensity,SpawnX,SpawnZ,GoalX,GoalZ," +
                        "GazeOriginX,GazeOriginY,GazeOriginZ,GazeDirX,GazeDirY,GazeDirZ," +
                        "LHandX,LHandY,LHandZ,LRotX,LRotY,LRotZ,RHandX,RHandY,RHandZ,RRotX,RRotY,RRotZ\n";

        File.WriteAllText(fullFilePath, header);

        activeBuffer.Clear();
        logTimer = 0f;
        int settingValue = AppManager.Instance.Settings.BufferSizeBeforeWrite;
        bufferLimit = Mathf.Clamp(settingValue, MinBufferSize, MaxBufferSize);
        isLogging = true;
        Debug.Log($"[LogManager] Recording to: {fullFilePath}");
    }

    public void EndLogging()
    {
        if (!isLogging) return;
        isLogging = false;
        if (activeBuffer.Count > 0) FlushBufferToDisk();
        try { lastWriteTask.Wait(); }
        catch (Exception e) { Debug.LogError($"[LogManager] Final write wait failed: {e.Message}"); }
        Debug.Log("[LogManager] Recording stopped.");
    }

    public void PauseLogging() { if (isLogging) paused = true; }
    public void ResumeLogging() { if (isLogging) paused = false; }

    public void ManualUpdate()
    {
        if (!isLogging || paused) return;

        logTimer += Time.deltaTime;
        if (logTimer < AppManager.Instance.Settings.DataLogInterval) return;
        logTimer = 0f;

        activeBuffer.Add(CaptureState(null));

        if (activeBuffer.Count >= bufferLimit) FlushBufferToDisk();
    }

    public void LogEvent(string input)
    {
        if (!isLogging) return;
        activeBuffer.Add(CaptureState(input));
        if (activeBuffer.Count >= bufferLimit) FlushBufferToDisk();
    }

    private void FlushBufferToDisk()
    {
        if (!isLogging && activeBuffer.Count == 0) return;

        string path = fullFilePath;

        List<LogFrame> dataToWrite = new List<LogFrame>(activeBuffer);
        activeBuffer.Clear();

        lastWriteTask = lastWriteTask.ContinueWith(_ =>
            Task.Run(() =>
            {
                try
                {
                    // Increased buffer estimate for extra columns
                    StringBuilder sb = new StringBuilder(dataToWrite.Count * 256);

                    foreach (var frame in dataToWrite)
                    {
                        frame.AppendToRow(sb);
                    }

                    lock (fileWriteLock)
                    {
                        File.AppendAllText(path, sb.ToString());
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[LogManager] Background write failed: {e.Message}");
                }
            })
        ).Unwrap();
    }
}