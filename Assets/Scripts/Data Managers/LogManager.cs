using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System;

public class LogManager
{
    public const int MinBufferSize = 1000;
    public const int MaxBufferSize = 30000; // Pre-allocate to prevent resizing (30k = ~5 mins at 90Hz)

    public struct LogFrame
    {
        public string Event;          // empty for normal frames; set for events
        public float GlobalTime;
        public Vector3 HeadPos;
        public Vector3 HeadRotEuler;
        public float StimulusIntensity;
    }

    // Double buffering logic
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

    public void BeginLogging()
    {
        if (isLogging) return;

        // Get Paths from Session Manager
        string folder = AppManager.Instance.Session.GetParticipantFolderPath();
        string fileName = AppManager.Instance.Session.GetCSVName();
        fullFilePath = Path.Combine(folder, fileName);

        // TODO: Ask if lab wants to use a settings snapshot
        /*try
        {
            // Shouldn't be necessary since data logging only occurs outside the title screen
            // AppManager.Instance.Settings.SaveToDisk();

            string sourceSettings = AppManager.Instance.Settings.FilePath;
            string destSettings = Path.Combine(folder, "settings_used_for_this_study.json");

            // Copy (overwrite = true ensures we don't crash if we restart the same participant)
            File.Copy(sourceSettings, destSettings, true);

            // Debug.Log($"[LogManager] Copied settings snapshot to: {destSettings}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[LogManager] Failed to create settings snapshot: {e.Message}");
        }*/

        // Write Header (Synchronous is fine here because it happens once)
        // Handle VR vs Desktop headers always log same data
        string header = "Event,GlobalTime,HeadX,HeadY,HeadZ,RotX,RotY,RotZ,StimulusIntensity\n";
        File.WriteAllText(fullFilePath, header);

        // Reset
        activeBuffer.Clear();
        logTimer = 0f;

        // Cache setting
        // If the user somehow saved "0" or "-1" in JSON, we clamp it here to be safe.
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

        // Ensure all queued writes finish before we consider logging done
        try { lastWriteTask.Wait(); }
        catch (Exception e) { Debug.LogError($"[LogManager] Final write wait failed: {e.Message}"); }

        Debug.Log("[LogManager] Recording stopped.");
    }

    public void PauseLogging()
    {
        if (!isLogging) return;

        paused = true;
    }

    public void ResumeLogging()
    {
        if (!isLogging) return;

        paused = false;
    }

    // Called manually by AppManager.Update
    public void ManualUpdate()
    {
        if (!isLogging || paused) return;

        // Throttle
        logTimer += Time.deltaTime;
        if (logTimer < AppManager.Instance.Settings.DataLogInterval) return;
        logTimer = 0f;

        // Capture
        Transform cameraT = AppManager.Instance.Player.CameraPosition();

        activeBuffer.Add(new LogFrame
        {
            GlobalTime = Time.time,
            HeadPos = cameraT.position,
            HeadRotEuler = cameraT.eulerAngles,
            StimulusIntensity = AppManager.Instance.Player.StimulusIntensity
        });

        // Safety Valve Flush
        // If we hit 30,000, dump it now to free up RAM. 
        // Otherwise, wait for EndLogging().
        if (activeBuffer.Count >= bufferLimit)
        {
            FlushBufferToDisk();
        }
    }

    public void LogEvent(string input)
    {
        if (!isLogging) return;

        string evt = (input ?? "").Replace("\n", " ").Replace("\r", " ").Replace(",", ";");

        Transform cameraT = AppManager.Instance.Player.CameraPosition();
        Vector3 pos = cameraT != null ? cameraT.position : Vector3.zero;
        Vector3 rot = cameraT != null ? cameraT.eulerAngles : Vector3.zero;

        activeBuffer.Add(new LogFrame
        {
            Event = evt,
            GlobalTime = Time.time,
            HeadPos = pos,
            HeadRotEuler = rot,
            StimulusIntensity = AppManager.Instance.Player.StimulusIntensity
        });

        if (activeBuffer.Count >= bufferLimit)
            FlushBufferToDisk();
    }


    private void FlushBufferToDisk()
    {
        if (!isLogging && activeBuffer.Count == 0) return;

        List<LogFrame> dataToWrite = new List<LogFrame>(activeBuffer);
        activeBuffer.Clear();

        // Chain writes so they run one after another
        lastWriteTask = lastWriteTask.ContinueWith(_ =>
            Task.Run(() =>
            {
                try
                {
                    StringBuilder sb = new StringBuilder(dataToWrite.Count * 100);

                    foreach (var frame in dataToWrite)
                    {
                        string evt = frame.Event ?? "";
                        evt = evt.Replace("\"", "\"\"");
                        sb.Append("\"").Append(evt).Append("\"").Append(",");

                        sb.Append(frame.GlobalTime.ToString("F3")).Append(",");
                        sb.Append(frame.HeadPos.x.ToString("F3")).Append(",");
                        sb.Append(frame.HeadPos.y.ToString("F3")).Append(",");
                        sb.Append(frame.HeadPos.z.ToString("F3")).Append(",");
                        sb.Append(frame.HeadRotEuler.x.ToString("F3")).Append(",");
                        sb.Append(frame.HeadRotEuler.y.ToString("F3")).Append(",");
                        sb.Append(frame.HeadRotEuler.z.ToString("F3")).Append(",");
                        sb.Append(frame.StimulusIntensity.ToString("F3"));
                        sb.AppendLine();
                    }


                    lock (fileWriteLock)
                    {
                        File.AppendAllText(fullFilePath, sb.ToString());
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