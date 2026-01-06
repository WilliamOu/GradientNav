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
        public float GlobalTime;        // Time.time
        public Vector3 HeadPos;         // Player Head Position
        public Vector3 HeadRotEuler;    // Player Rotation (Euler is easier for Python analysis than Quat)
        public float StimulusIntensity; // The brightness value they saw

        // TODO: Add inputs here later if needed
    }

    // Double buffering logic
    private List<LogFrame> activeBuffer;
    private bool isLogging = false;
    private bool paused = false;
    private string fullFilePath;
    private float logTimer;
    private int bufferLimit;

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
        string header = "GlobalTime,HeadX,HeadY,HeadZ,RotX,RotY,RotZ,StimulusIntensity\n";
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

        // Flush whatever is left
        if (activeBuffer.Count > 0) FlushBufferToDisk();

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

    private async void FlushBufferToDisk()
    {
        // Move data to a temp list so the main thread can keep recording to a new list
        List<LogFrame> dataToWrite = new List<LogFrame>(activeBuffer);
        activeBuffer.Clear();

        // Process (Background Thread)
        await Task.Run(() =>
        {
            try
            {
                StringBuilder sb = new StringBuilder(dataToWrite.Count * 100); // Pre-allocate approx size

                foreach (var frame in dataToWrite)
                {
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

                File.AppendAllText(fullFilePath, sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogError($"[LogManager] Background write failed: {e.Message}");
            }
        });
    }
}