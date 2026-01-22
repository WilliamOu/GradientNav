using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class ReplaySelectionManager : MonoBehaviour
{
    private const string ReplaysFolderName = "Replays";
    private const string XriCsvSuffix = "_XRI.csv";
    private const string XriBinSuffix = "_XRI.bin";
    private const string ShadowBinSuffix = "_Shadow.bin";

    // Binary Device Type IDs (The "Limb Labeling")
    private const byte DeviceId_Head = 0;
    private const byte DeviceId_LeftHand = 1;
    private const byte DeviceId_RightHand = 2;
    private const byte DeviceId_Gaze = 3;

    [Header("UI")]
    [SerializeField] private TMP_Dropdown replayDropdown;
    [SerializeField] private Button startReplayButton;
    [SerializeField] private TitleSceneManager titleScreenReference;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = true;

    private string PersistentRoot => Application.persistentDataPath;
    private string ReplaysRoot => Path.Combine(PersistentRoot, ReplaysFolderName);

    private readonly List<string> replayFolderPaths = new List<string>();

    private void Awake()
    {
        if (startReplayButton != null)
            startReplayButton.onClick.AddListener(OnStartReplayClicked);
    }

    private void Start()
    {
        RefreshDropdown();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus) RefreshDropdown();
    }

    private void RefreshDropdown()
    {
        if (!Directory.Exists(ReplaysRoot)) Directory.CreateDirectory(ReplaysRoot);

        replayFolderPaths.Clear();
        var options = new List<TMP_Dropdown.OptionData>();

        try
        {
            var dirs = Directory.GetDirectories(ReplaysRoot);
            Array.Sort(dirs, (a, b) => Directory.GetCreationTime(b).CompareTo(Directory.GetCreationTime(a)));

            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;

                string folderName = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(folderName)) continue;

                bool hasXri = Directory.GetFiles(dir, "*" + XriCsvSuffix).Any() || Directory.GetFiles(dir, "*" + XriBinSuffix).Any();
                bool hasShadow = Directory.GetFiles(dir, "*" + ShadowBinSuffix).Any();

                if (hasXri && hasShadow)
                {
                    replayFolderPaths.Add(dir);
                    options.Add(new TMP_Dropdown.OptionData(folderName));
                }
            }
        }
        catch (Exception e) { titleScreenReference.WriteMessage($"Scan error: {e.Message}", Color.red, 4); }

        if (replayDropdown != null)
        {
            replayDropdown.ClearOptions();
            replayDropdown.AddOptions(options);
            // titleScreenReference.WriteMessage($"Found {options.Count} replay(s).", Color.white, 2);
        }
    }

    private void OnStartReplayClicked()
    {
        if (replayDropdown == null || replayFolderPaths.Count == 0) return;

        int idx = replayDropdown.value;
        if (idx < 0 || idx >= replayFolderPaths.Count) return;

        string folderPath = replayFolderPaths[idx];

        try
        {
            EnsureProcessedReplayFiles(folderPath);

            // Hand off to AppManager as requested
            if (AppManager.Instance != null && AppManager.Instance.Replay != null)
            {
                AppManager.Instance.Replay.SetFolderPath(folderPath);
                titleScreenReference.WriteMessage("Replay Set. Starting...", Color.green);
            }
            else
            {
                Debug.LogError("[ReplaySelection] AppManager.Instance.Replay is null!");
                titleScreenReference.WriteMessage("Error: AppManager not found.");
            }
        }
        catch (Exception e)
        {
            titleScreenReference.WriteMessage($"Load failed: {e.Message}", Color.red, 4);
            Debug.LogException(e);
        }

        SceneManager.LoadScene("Replay Scene"); // Ensure you add this scene to the scene manager!
    }

    private void EnsureProcessedReplayFiles(string folderPath)
    {
        // 1. Check Shadow
        string shadowBin = Directory.GetFiles(folderPath, "*" + ShadowBinSuffix).FirstOrDefault();
        if (string.IsNullOrEmpty(shadowBin))
            throw new FileNotFoundException($"Shadow bin missing in {folderPath}");

        // 2. Check XRI Bin
        string xriBinPath = Path.Combine(folderPath, Path.GetFileNameWithoutExtension(Directory.GetFiles(folderPath, "*" + XriCsvSuffix).FirstOrDefault() ?? "Session") + XriBinSuffix);

        // If bin exists, we are good. (In a real tool, maybe check version number to force re-convert if schema changes)
        if (File.Exists(xriBinPath)) return;

        // 3. Convert XRI CSV -> Bin
        string xriCsv = Directory.GetFiles(folderPath, "*" + XriCsvSuffix).FirstOrDefault();
        if (string.IsNullOrEmpty(xriCsv))
            throw new FileNotFoundException($"XRI CSV missing in {folderPath}");

        // Fix path naming if needed
        string baseName = Path.GetFileNameWithoutExtension(xriCsv);
        if (baseName.EndsWith("_XRI")) baseName = baseName.Substring(0, baseName.Length - 4);
        string outBin = Path.Combine(folderPath, baseName + XriBinSuffix);

        titleScreenReference.WriteMessage("Processing XRI CSV...", Color.yellow, 4);
        ConvertXriCsvToBin(xriCsv, outBin);
    }

    // --- CSV CONVERSION LOGIC ---

    private void ConvertXriCsvToBin(string csvPath, string outBinPath)
    {
        using var fs = new FileStream(outBinPath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // FIX: Use LOCAL constants, don't rely on ReplayManager being compiled/public yet
        bw.Write(ReplayManager.XriBinMagic);
        bw.Write(ReplayManager.XriBinVersion);

        long countPos = fs.Position;
        bw.Write(0); // Placeholder for frame count

        using var sr = new StreamReader(csvPath);
        string headerLine = sr.ReadLine();
        if (headerLine == null) throw new Exception("Empty CSV");

        // USE NEW ROBUST SPLIT
        var headers = SplitCsvLine(headerLine);
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++) map[headers[i].Trim()] = i;

        var keys = new ColumnKeys();

        // Check required columns
        int idxTicks = RequireCol(map, keys.StopwatchTicks);
        int idxState = map.ContainsKey(keys.State) ? map[keys.State] : -1;

        // Poses
        PoseCols head = GetPoseCols(map, keys.HeadX, keys.HeadY, keys.HeadZ, keys.HeadRotX, keys.HeadRotY, keys.HeadRotZ);
        PoseCols lHand = GetPoseCols(map, keys.LX, keys.LY, keys.LZ, keys.LRotX, keys.LRotY, keys.LRotZ);
        PoseCols rHand = GetPoseCols(map, keys.RX, keys.RY, keys.RZ, keys.RRotX, keys.RRotY, keys.RRotZ);

        // Gaze
        int idxGazeOrigX = RequireCol(map, keys.GazeOrigX);
        int idxGazeOrigY = RequireCol(map, keys.GazeOrigY);
        int idxGazeOrigZ = RequireCol(map, keys.GazeOrigZ);
        int idxGazeDirX = RequireCol(map, keys.GazeDirX);
        int idxGazeDirY = RequireCol(map, keys.GazeDirY);
        int idxGazeDirZ = RequireCol(map, keys.GazeDirZ);

        // Flags: Bit 0 = HasState
        bw.Write((byte)(idxState >= 0 ? 1 : 0));

        int frames = 0;
        string line;
        while ((line = sr.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cols = SplitCsvLine(line);
            if (cols.Length < headers.Length) continue;

            if (long.TryParse(cols[idxTicks], out long ticks))
            {
                bw.Write(ticks);

                if (idxState >= 0)
                    bw.Write(EncodeState(cols[idxState]));

                // Write Poses with ID tags ("Which limb is limb")
                bw.Write(DeviceId_Head);
                WritePoseFromEuler(bw, cols, head);

                bw.Write(DeviceId_LeftHand);
                WritePoseFromEuler(bw, cols, lHand);

                bw.Write(DeviceId_RightHand);
                WritePoseFromEuler(bw, cols, rHand);

                // Write Gaze
                bw.Write(DeviceId_Gaze);
                WriteVector3(bw, cols, idxGazeOrigX, idxGazeOrigY, idxGazeOrigZ);
                WriteVector3(bw, cols, idxGazeDirX, idxGazeDirY, idxGazeDirZ);

                frames++;
            }
        }

        fs.Position = countPos;
        bw.Write(frames);

        if (verboseLogs) Debug.Log($"[Replay] Converted {frames} frames to binary with Gaze.");
    }

    private struct PoseCols { public int x, y, z, rx, ry, rz; }

    private PoseCols GetPoseCols(Dictionary<string, int> map, string x, string y, string z, string rx, string ry, string rz)
    {
        return new PoseCols
        {
            x = RequireCol(map, x),
            y = RequireCol(map, y),
            z = RequireCol(map, z),
            rx = RequireCol(map, rx),
            ry = RequireCol(map, ry),
            rz = RequireCol(map, rz)
        };
    }

    private void WritePoseFromEuler(BinaryWriter bw, string[] cols, PoseCols p)
    {
        // Position
        bw.Write(ParseFloat(cols[p.x]));
        bw.Write(ParseFloat(cols[p.y]));
        bw.Write(ParseFloat(cols[p.z]));

        // Rotation (Euler -> Quat)
        float ex = ParseFloat(cols[p.rx]);
        float ey = ParseFloat(cols[p.ry]);
        float ez = ParseFloat(cols[p.rz]);
        Quaternion q = Quaternion.Euler(ex, ey, ez);

        bw.Write(q.x); bw.Write(q.y); bw.Write(q.z); bw.Write(q.w);
    }

    private void WriteVector3(BinaryWriter bw, string[] cols, int ix, int iy, int iz)
    {
        bw.Write(ParseFloat(cols[ix]));
        bw.Write(ParseFloat(cols[iy]));
        bw.Write(ParseFloat(cols[iz]));
    }

    private static int RequireCol(Dictionary<string, int> map, string name)
    {
        if (!map.ContainsKey(name)) throw new Exception($"Missing CSV column: {name}");
        return map[name];
    }

    private static float ParseFloat(string s) => float.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out float v) ? v : 0f;

    private static byte EncodeState(string s)
    {
        s = s.Trim().ToUpperInvariant();
        if (s.Contains("TRAINING")) return 0;
        if (s.Contains("IDLE")) return 1;
        if (s.Contains("ORIENT")) return 2;
        if (s.Contains("PAUSED")) return 3;
        if (s.Contains("TRIAL")) return 4;
        return 255;
    }

    private static string[] SplitCsvLine(string line)
    {
        // If no quotes exist, fast path
        if (line.IndexOf('"') < 0) return line.Split(',');

        var result = new List<string>();
        bool inQuotes = false;
        int start = 0;

        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (line[i] == ',' && !inQuotes)
            {
                result.Add(Unquote(line.Substring(start, i - start)));
                start = i + 1;
            }
        }
        // Add last column
        result.Add(Unquote(line.Substring(start)));
        return result.ToArray();
    }

    private static string Unquote(string val)
    {
        val = val.Trim();
        if (val.Length >= 2 && val.StartsWith("\"") && val.EndsWith("\""))
        {
            // Remove surrounding quotes and unescape double quotes
            return val.Substring(1, val.Length - 2).Replace("\"\"", "\"");
        }
        return val;
    }

    private class ColumnKeys
    {
        public string StopwatchTicks = "StopwatchTicks";
        public string State = "State";

        // Head
        public string HeadX = "HeadX";
        public string HeadY = "HeadY";
        public string HeadZ = "HeadZ";
        public string HeadRotX = "RotX";
        public string HeadRotY = "RotY";
        public string HeadRotZ = "RotZ";

        // Left Hand
        public string LX = "LHandX";
        public string LY = "LHandY";
        public string LZ = "LHandZ";
        public string LRotX = "LRotX";
        public string LRotY = "LRotY";
        public string LRotZ = "LRotZ";

        // Right Hand
        public string RX = "RHandX";
        public string RY = "RHandY";
        public string RZ = "RHandZ";
        public string RRotX = "RRotX";
        public string RRotY = "RRotY";
        public string RRotZ = "RRotZ";

        // Gaze (Added)
        public string GazeOrigX = "GazeOriginX";
        public string GazeOrigY = "GazeOriginY";
        public string GazeOrigZ = "GazeOriginZ";
        public string GazeDirX = "GazeDirX";
        public string GazeDirY = "GazeDirY";
        public string GazeDirZ = "GazeDirZ";
    }
}