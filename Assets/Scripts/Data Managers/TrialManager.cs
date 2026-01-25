using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

public class TrialSpec
{
    public int MapTypeIndex;
    public Vector2 SpawnXZ;
    public Vector2 CenterXZ;
    public Vector2? GoalOverride;
    public List<Vector2> ExtraGoals = new();
    public List<PeakSpec> Peaks;
    public float? SigmaOverride;
}

public enum TrialPlanMode
{
    RandomNoSeed,
    RandomSeeded,
    Csv
}

public class TrialManager : MonoBehaviour
{
    public TrialPlanMode CurrentPlanMode { get; private set; }
    private List<TrialSpec> _csvTrials;

    [SerializeField] private float minStartDistance = 1f;

    public void Init()
    {
        // Determine mode and load data if necessary
        CurrentPlanMode = TrialPlanMode.RandomNoSeed;
        _csvTrials = null;

        if (TryGetCsvPathFromSettings(out string csvPath))
        {
            if (File.Exists(csvPath))
            {
                try
                {
                    _csvTrials = LoadTrialsFromCsv(csvPath);
                    if (_csvTrials != null && _csvTrials.Count > 0)
                        CurrentPlanMode = TrialPlanMode.Csv;
                    else
                        Debug.LogWarning("CSV had no valid trials, falling back to random.");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to load CSV trials: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning("Selected CSV does not exist, falling back to random.");
            }
        }
        else
        {
            CurrentPlanMode = (AppManager.Instance.Settings.TrialSourceIndex == 1)
                ? TrialPlanMode.RandomSeeded
                : TrialPlanMode.RandomNoSeed;
        }
    }

    public int GetTotalTrialCount()
    {
        return (CurrentPlanMode == TrialPlanMode.Csv)
            ? _csvTrials.Count
            : AppManager.Instance.Settings.TrialCount;
    }

    public TrialSpec GetTrial(int trialIndex)
    {
        if (CurrentPlanMode == TrialPlanMode.Csv)
        {
            return _csvTrials[trialIndex];
        }
        else
        {
            return GenerateRandomTrial(trialIndex);
        }
    }

    private TrialSpec GenerateRandomTrial(int tIndex)
    {
        float width = AppManager.Instance.Settings.MapWidth;
        float length = AppManager.Instance.Settings.MapLength;
        int mapType = AppManager.Instance.Settings.MapTypeIndex;

        float spawnRadiusX = (width / 2f) * 0.9f;
        float spawnRadiusZ = (length / 2f) * 0.9f;

        Vector2 s = Vector2.zero;
        Vector2 t = Vector2.zero;
        Vector2 randomMapCenter = Vector2.zero;
        List<PeakSpec> peaks = null;

        // RNG Setup
        System.Random rng = null;
        if (CurrentPlanMode == TrialPlanMode.RandomSeeded)
        {
            int baseSeed = AppManager.Instance.Settings.Seed;
            int trialSeed = unchecked(baseSeed * 486187739 + (tIndex + 1) * 16777619);
            rng = new System.Random(trialSeed);
        }

        float NextFloat(System.Random r, float min, float max)
        {
            if (r == null) return UnityEngine.Random.Range(min, max);
            return (float)(min + (max - min) * r.NextDouble());
        }

        // Attempt generation loop
        int safetyBreak = 0;
        do
        {
            s = new Vector2(
                NextFloat(rng, -spawnRadiusX, spawnRadiusX),
                NextFloat(rng, -spawnRadiusZ, spawnRadiusZ)
            );

            randomMapCenter = new Vector2(
                NextFloat(rng, -spawnRadiusX, spawnRadiusX),
                NextFloat(rng, -spawnRadiusZ, spawnRadiusZ)
            );

            peaks = null;
            if (mapType == 3) // MultiPeak
            {
                float mapRadius = Mathf.Min(width, length) / 2f;
                int peaksSeed;

                if (CurrentPlanMode == TrialPlanMode.RandomSeeded)
                    peaksSeed = unchecked(AppManager.Instance.Settings.Seed * 486187739 + (tIndex + 1) * 16777619);
                else
                    peaksSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

                peaks = MultiPeakSpecFactory.Create(peaksSeed, mapRadius, AppManager.Instance.Settings.PeakCount);
            }

            t = StimulusManager.ComputePrimaryTarget(
                mapType,
                width,
                length,
                randomMapCenter,
                peaks
            );

            safetyBreak++;
        }
        while (Vector2.Distance(s, t) < minStartDistance && safetyBreak < 100);

        if (safetyBreak >= 100) Debug.LogWarning("Could not find valid start pos!");

        return new TrialSpec
        {
            MapTypeIndex = mapType,
            SpawnXZ = s,
            CenterXZ = randomMapCenter,
            GoalOverride = null,
            Peaks = peaks
        };
    }

    // ---------------------------------------------------------
    // CSV PARSING LOGIC
    // ---------------------------------------------------------

    private bool TryGetCsvPathFromSettings(out string csvPath)
    {
        csvPath = null;
        var setting = AppManager.Instance.Settings.SettingsList
            .OfType<EnumSetting>()
            .FirstOrDefault(s => s.Name == "Trial Source");

        if (setting == null || setting.Options == null || setting.Options.Count == 0) return false;

        int idx = Mathf.Clamp(setting.SelectedIndex, 0, setting.Options.Count - 1);
        string selected = setting.Options[idx];

        if (string.IsNullOrWhiteSpace(selected)) return false;
        if (!selected.EndsWith("(CSV)", StringComparison.OrdinalIgnoreCase)) return false;

        string name = selected.Substring(0, selected.Length - "(CSV)".Length).Trim();
        if (string.IsNullOrWhiteSpace(name)) return false;

        csvPath = Path.Combine(AppManager.Instance.Settings.TrialsFolderPath, name + ".csv");
        return true;
    }

    private List<TrialSpec> LoadTrialsFromCsv(string csvPath)
    {
        var lines = File.ReadAllLines(csvPath);
        var trials = new List<TrialSpec>();

        if (lines.Length == 0) return trials;

        int headerLineIndex = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var ln = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(ln)) continue;
            if (ln.StartsWith("#")) continue;
            headerLineIndex = i;
            break;
        }

        if (headerLineIndex < 0) return trials;

        var header = SplitCsvLine(lines[headerLineIndex]).Select(h => h.Trim().Trim('"')).ToList();
        int Col(string name) => header.FindIndex(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));

        int cMapType = Col("MapType");
        int cSpawnX = Col("SpawnX");
        int cSpawnZ = Col("SpawnZ");
        int cCenterX = Col("CenterX");
        int cCenterZ = Col("CenterZ");
        int cGoals = Col("Goals");
        int cPeaks = Col("Peaks");
        int cSigma = Col("Sigma");

        if (cMapType < 0 || cSpawnX < 0 || cSpawnZ < 0)
            throw new Exception("CSV missing required columns.");

        for (int i = headerLineIndex + 1; i < lines.Length; i++)
        {
            string ln = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(ln) || ln.StartsWith("#")) continue;

            var cells = SplitCsvLine(lines[i]);
            string Get(int col) => (col < 0 || col >= cells.Count) ? "" : cells[col];

            int mapType = ParseMapTypeIndex(Get(cMapType));
            if (!TryParseFloat(Get(cSpawnX), out float sx) || !TryParseFloat(Get(cSpawnZ), out float sz)) continue;

            float cx = 0f, cz = 0f;
            TryParseFloat(Get(cCenterX), out cx);
            TryParseFloat(Get(cCenterZ), out cz);

            var goals = ParseVector2List(Get(cGoals));
            Vector2? goalOverride = (goals.Count > 0) ? goals[0] : (Vector2?)null;

            var peaks = ParsePeakList(Get(cPeaks));

            if (mapType == 3 && (peaks == null || peaks.Count == 0))
            {
                Debug.LogWarning($"Skipping line {i + 1}: Multi-Peak requires Peaks.");
                continue;
            }
            
            float? sigmaOverride = null;
            if (cSigma >= 0 && TryParseFloat(Get(cSigma), out float sVal))
            {
                sigmaOverride = sVal;
            }

            trials.Add(new TrialSpec
            {
                MapTypeIndex = mapType,
                SpawnXZ = new Vector2(sx, sz),
                CenterXZ = new Vector2(cx, cz),
                GoalOverride = goalOverride,
                ExtraGoals = (goals.Count > 1) ? goals.Skip(1).ToList() : new List<Vector2>(),
                Peaks = peaks,
                SigmaOverride = sigmaOverride // NEW: Assign the value
            });
        }
        return trials;
    }

    // --- Static Helpers ---
    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        if (line == null) return result;
        bool inQuotes = false;
        var cur = new System.Text.StringBuilder();
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes) { result.Add(cur.ToString().Trim()); cur.Clear(); }
            else cur.Append(c);
        }
        result.Add(cur.ToString().Trim());
        return result;
    }

    private static bool TryParseFloat(string s, out float f) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f);

    private static int ParseMapTypeIndex(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        s = s.Trim().Trim('"');
        if (int.TryParse(s, out int idx)) return Mathf.Clamp(idx, 0, StimulusManager.MapTypes.Count - 1);
        int found = StimulusManager.MapTypes.FindIndex(m => string.Equals(m, s, StringComparison.OrdinalIgnoreCase));
        return (found >= 0) ? found : 0;
    }

    private static List<Vector2> ParseVector2List(string s)
    {
        var list = new List<Vector2>();
        if (string.IsNullOrWhiteSpace(s)) return list;
        var items = s.Trim().Trim('"').Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var item in items)
        {
            var parts = item.Replace(",", " ").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && TryParseFloat(parts[0], out float x) && TryParseFloat(parts[1], out float z))
                list.Add(new Vector2(x, z));
        }
        return list;
    }

    private static List<PeakSpec> ParsePeakList(string s)
    {
        var list = new List<PeakSpec>();
        if (string.IsNullOrWhiteSpace(s)) return list;
        var items = s.Trim().Trim('"').Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var item in items)
        {
            var parts = item.Replace(",", " ").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && TryParseFloat(parts[0], out float x) && TryParseFloat(parts[1], out float z) && TryParseFloat(parts[2], out float a))
                list.Add(new PeakSpec(new Vector2(x, z), a));
        }
        return list;
    }
}