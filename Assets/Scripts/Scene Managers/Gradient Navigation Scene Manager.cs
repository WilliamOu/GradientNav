using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GradientNavigationSceneManager : MonoBehaviour
{
    private enum TrialPlanMode
    {
        RandomNoSeed,
        RandomSeeded,
        Csv
    }

    private sealed class TrialSpec
    {
        public int MapTypeIndex;
        public Vector2 SpawnXZ;
        public Vector2 CenterXZ;                  // used by non-multipeak maps
        public Vector2? GoalOverride;             // first goal from Goals list, if any
        public List<Vector2> ExtraGoals = new();  // optional, not used yet
        public List<PeakSpec> Peaks;              // only for Multi-Peak
    }

    private TrialPlanMode planMode;
    private List<TrialSpec> csvTrials;

    [SerializeField] private float minStartDistance = 1f;

    // State
    private SessionDataManager.GameState state;

    private int trialIndex;
    private int attemptsRemaining;
    private float timeRemaining;

    private bool trialComplete;

    // Current trial randomized data
    private Vector2 startXZ;
    private Vector2 targetXZ;

    // Pause return position (VR unpause uses orient-walk back here)
    private Vector2 pausedReturnXZ;

    private void Start()
    {
        SetState(SessionDataManager.GameState.Idle);
        AppManager.Instance.Player.SpawnPlayer(Vector3.zero, Quaternion.identity);
        StartCoroutine(RunAllTrials());
    }

    private void Update()
    {
        AppManager.Instance.Logger.ManualUpdate();
        AppManager.Instance.Player.Minimap.ManualUpdate();

        // Pause/unpause toggle
        if (GetPauseToggleInput())
        {
            if (state == SessionDataManager.GameState.Trial) Pause();
            else if (state == SessionDataManager.GameState.Paused) Unpause();
            return;
        }

        if (AppManager.Instance.Session.IsVRMode && GetRecenteringInput())
        {
            AppManager.Instance.Player.TeleportVRToCoordinates(0, 0);
        }

        // Only Trial state runs game loop logic
        if (state != SessionDataManager.GameState.Trial) return;

        AppManager.Instance.Player.UpdateStimulusUI();

        timeRemaining -= Time.deltaTime;
        if (timeRemaining <= 0f)
        {
            Debug.Log($"[Trial {trialIndex + 1}] Time expired.");
            EndTrial();
            return;
        }

        if (GetSubmitInput())
        {
            HandleSubmission();
        }
    }

    private IEnumerator RunAllTrials()
    {
        planMode = TrialPlanMode.RandomNoSeed;
        csvTrials = null;

        if (TryGetCsvPathFromSettings(out string csvPath))
        {
            if (File.Exists(csvPath))
            {
                try
                {
                    csvTrials = LoadTrialsFromCsv(csvPath);
                    if (csvTrials != null && csvTrials.Count > 0)
                        planMode = TrialPlanMode.Csv;
                    else
                        Debug.LogWarning("CSV had no valid trials, falling back to random (no seed).");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to load CSV trials, falling back to random (no seed). Error: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning("Selected CSV does not exist, falling back to random (no seed).");
            }
        }
        else
        {
            // Not a CSV selection, interpret first two options
            // You currently store "TrialSourceIndex" in Settings, so:
            planMode = (AppManager.Instance.Settings.TrialSourceIndex == 1)
                ? TrialPlanMode.RandomSeeded
                : TrialPlanMode.RandomNoSeed;
        }

        AppManager.Instance.Player.SetUIMessage("", Color.white, -1);
        AppManager.Instance.Logger.BeginLogging();
        int totalTrials = (planMode == TrialPlanMode.Csv) ? csvTrials.Count : AppManager.Instance.Settings.TrialCount;

        for (trialIndex = 0; trialIndex < totalTrials; trialIndex++)
        {
            // Pick new randomized positions every loop
            TrialSpec spec;

            if (planMode == TrialPlanMode.Csv)
            {
                spec = csvTrials[trialIndex];

                // Apply CSV-defined map immediately here so intensity matches before teleport
                AppManager.Instance.Stimulus.GenerateMap(
                    spec.MapTypeIndex,
                    AppManager.Instance.Settings.MapWidth,
                    AppManager.Instance.Settings.MapLength,
                    spec.CenterXZ,
                    goalOverride: spec.GoalOverride,
                    multiPeakSpecs: spec.Peaks
                );

                startXZ = spec.SpawnXZ;
                targetXZ = AppManager.Instance.Session.GoalPosition; // set by GenerateMap (override or map target)
            }
            else
            {
                // Random path (seeded or not)
                SetupPositions(trialIndex, planMode, out startXZ, out targetXZ);
            }

            AppManager.Instance.Player.Minimap.RefreshMinimap();

            AppManager.Instance.Session.TrialNumber = trialIndex + 1;
            AppManager.Instance.Session.SpawnPosition = startXZ;
            AppManager.Instance.Session.GoalPosition = targetXZ;

            // Reset per-trial counters
            attemptsRemaining = AppManager.Instance.Settings.ParticipantMaxTestCount;
            timeRemaining = AppManager.Instance.Settings.TimeToSeek;
            trialComplete = false;

            // Move player to start:
            if (!AppManager.Instance.Session.IsVRMode)
                AppManager.Instance.Player.Teleport(startXZ.x, startXZ.y);
            else
                yield return WalkOrientTo(startXZ);

            // Start trial
            SetState(SessionDataManager.GameState.Trial);
            AppManager.Instance.Logger.LogEvent($"TRIAL_START {trialIndex}");

            // Wait until the trial truly completes (pause does not count)
            yield return new WaitUntil(() => trialComplete);

            SetState(SessionDataManager.GameState.Idle);
        }

        // All trials complete
        SetState(SessionDataManager.GameState.Idle);
        AppManager.Instance.Logger.EndLogging();
        SceneManager.LoadScene("Closing Scene");
    }

    private void SetState(SessionDataManager.GameState gameState)
    {
        state = gameState;
        AppManager.Instance.Session.State = gameState;
    }

    private void HandleSubmission()
    {
        float currentIntensity = AppManager.Instance.Player.StimulusIntensity;
        bool isSuccess = currentIntensity >= AppManager.Instance.Settings.SuccessThreshold;

        if (isSuccess)
        {
            Debug.Log($"[Trial {trialIndex + 1}] Success.");
            AppManager.Instance.Logger.LogEvent($"TRIAL_SUCCESS {trialIndex}");
            EndTrial();
            return;
        }

        attemptsRemaining--;

        // -1 means infinite attempts
        if (attemptsRemaining != -1 && attemptsRemaining <= 0)
        {
            Debug.Log($"[Trial {trialIndex + 1}] Failed (no attempts remaining).");
            AppManager.Instance.Logger.LogEvent($"TRIAL_FAIL {trialIndex}");
            EndTrial();
        }
    }

    private void EndTrial()
    {
        if (state != SessionDataManager.GameState.Trial) return;

        // Stop trial logic immediately
        SetState(SessionDataManager.GameState.Idle);
        trialComplete = true;
    }

    // ----------- Pause / Unpause -----------
    private void Pause()
    {
        if (state != SessionDataManager.GameState.Trial) return;

        // Save where we were when paused (XZ)
        Vector3 camPos = AppManager.Instance.Player.CameraPosition().position;
        pausedReturnXZ = new Vector2(camPos.x, camPos.z);

        SetState(SessionDataManager.GameState.Paused);

        // Desktop: toggle movement
        if (!AppManager.Instance.Session.IsVRMode)
            AppManager.Instance.Player.ToggleMovement();

        // VR: no teleport, just halt trial logic
        AppManager.Instance.Player.SetUIMessage("Paused", Color.white, -1);
        AppManager.Instance.Logger.LogEvent("PAUSED");
        AppManager.Instance.Player.EnableBlackscreen();
    }

    private void Unpause()
    {
        if (state != SessionDataManager.GameState.Paused) return;

        AppManager.Instance.Player.SetUIMessage("", Color.white, -1);
        AppManager.Instance.Logger.LogEvent("UNPAUSED");
        AppManager.Instance.Player.DisableBlackscreen();

        if (!AppManager.Instance.Session.IsVRMode)
        {
            // Desktop: restore movement + resume trial
            AppManager.Instance.Player.ToggleMovement();
            SetState(SessionDataManager.GameState.Trial);
            return;
        }

        // VR: walk-orient back to where they paused, then resume
        SetState(SessionDataManager.GameState.Trial); // StartCoroutine(UnpauseVRRoutine()); (CURRENTLY DISABLED)
    }

    private IEnumerator UnpauseVRRoutine()
    {
        yield return WalkOrientTo(pausedReturnXZ);
        SetState(SessionDataManager.GameState.Trial);
    }

    // ----------- Orientation (VR only) -----------

    private IEnumerator WalkOrientTo(Vector2 xz)
    {
        if (!AppManager.Instance.Session.IsVRMode)
            yield break; // Orientation must never happen on desktop

        SetState(SessionDataManager.GameState.Orient);
        AppManager.Instance.Logger.LogEvent($"ORIENTATION_START {trialIndex}");

        // Walk-only orientation
        yield return AppManager.Instance.Orientation.WalkToLocation(xz.x, xz.y);

        AppManager.Instance.Logger.LogEvent($"ORIENTATION_END {trialIndex}");
        SetState(SessionDataManager.GameState.Idle);
    }

    // ----------- Randomization -----------

    private void SetupPositions(int tIndex, TrialPlanMode mode, out Vector2 outStartXZ, out Vector2 outTargetXZ)
    {
        float width = AppManager.Instance.Settings.MapWidth;
        float length = AppManager.Instance.Settings.MapLength;
        int mapType = AppManager.Instance.Settings.MapTypeIndex;

        float spawnRadiusX = (width / 2f) * 0.9f;
        float spawnRadiusZ = (length / 2f) * 0.9f;

        Vector2 s = Vector2.zero;
        Vector2 t = Vector2.zero;

        // RNG choice
        System.Random rng = null;
        if (mode == TrialPlanMode.RandomSeeded)
        {
            // Per-trial seed so trial order changes don’t collapse everything
            int baseSeed = AppManager.Instance.Settings.Seed;
            int trialSeed = unchecked(baseSeed * 486187739 + (tIndex + 1) * 16777619);
            rng = new System.Random(trialSeed);
        }

        float NextFloat(System.Random r, float min, float max)
        {
            if (r == null) return UnityEngine.Random.Range(min, max);
            return (float)(min + (max - min) * r.NextDouble());
        }

        int safetyBreak = 0;
        do
        {
            s = new Vector2(
                NextFloat(rng, -spawnRadiusX, spawnRadiusX),
                NextFloat(rng, -spawnRadiusZ, spawnRadiusZ)
            );

            Vector2 randomMapCenter = new Vector2(
                NextFloat(rng, -spawnRadiusX, spawnRadiusX),
                NextFloat(rng, -spawnRadiusZ, spawnRadiusZ)
            );

            // If you want Multi-Peak to be deterministic too, provide peaks when mapType == 3.
            // Otherwise it will use whatever default/fallback you coded for empty peaks.
            IReadOnlyList<PeakSpec> peaks = null;
            if (mapType == 3)
            {
                float mapRadius = Mathf.Min(width, length) / 2f;
                int peaksSeed;

                if (mode == TrialPlanMode.RandomSeeded)
                {
                    peaksSeed = unchecked(AppManager.Instance.Settings.Seed * 486187739 + (tIndex + 1) * 16777619);
                }
                else
                {
                    peaksSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                }
                peaks = MultiPeakSpecFactory.Create(peaksSeed, mapRadius, AppManager.Instance.Settings.PeakCount);
            }

            AppManager.Instance.Stimulus.GenerateMap(
                mapType,
                width,
                length,
                randomMapCenter,
                goalOverride: null,
                multiPeakSpecs: peaks
            );

            t = AppManager.Instance.Stimulus.GetTargetPosition();

            safetyBreak++;
        }
        while (Vector2.Distance(s, t) < minStartDistance && safetyBreak < 100);

        if (safetyBreak >= 100) Debug.LogWarning("Could not find valid start pos!");

        outStartXZ = s;
        outTargetXZ = t;
    }


    // TODO: For multiple test attempts, send a message 

    private bool GetSubmitInput()
    {
        if (state != SessionDataManager.GameState.Trial) return false;

        // Desktop
        if (!AppManager.Instance.Session.IsVRMode && Input.GetKeyDown(KeyCode.Return))
            return true;

        // VR
        bool leftPressed = AppManager.Instance.LeftActivate.action != null &&
                           AppManager.Instance.LeftActivate.action.WasPressedThisFrame();

        bool rightPressed = AppManager.Instance.RightActivate.action != null &&
                            AppManager.Instance.RightActivate.action.WasPressedThisFrame();

        return leftPressed || rightPressed;
    }

    private bool GetPauseToggleInput()
    {
        if (!AppManager.Instance.Settings.EnablePause) return false;

        // Valid states only
        if (state != SessionDataManager.GameState.Trial && state != SessionDataManager.GameState.Paused) return false;

        // Desktop
        if (!AppManager.Instance.Session.IsVRMode)
            return Input.GetKeyDown(KeyCode.Space);

        // VR
        if (AppManager.Instance.LeftSelect.action != null &&
            AppManager.Instance.LeftSelect.action.WasPressedThisFrame())
        {
            return true;
        }

        return false;
    }

    private bool GetRecenteringInput()
    {
        return Input.GetKeyDown(KeyCode.R) || (AppManager.Instance.Session.IsVRMode && AppManager.Instance.RightSelect.action != null && AppManager.Instance.RightSelect.action.WasPressedThisFrame());
    }

    private List<TrialSpec> LoadTrialsFromCsv(string csvPath)
    {
        var lines = File.ReadAllLines(csvPath);
        var trials = new List<TrialSpec>();

        if (lines.Length == 0) return trials;

        // Skip empty/comment lines at top until header
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

        var header = SplitCsvLine(lines[headerLineIndex])
            .Select(h => h.Trim().Trim('"'))
            .ToList();

        int Col(string name)
        {
            return header.FindIndex(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));
        }

        int cMapType = Col("MapType");
        int cSpawnX = Col("SpawnX");
        int cSpawnZ = Col("SpawnZ");
        int cCenterX = Col("CenterX");
        int cCenterZ = Col("CenterZ");
        int cGoals = Col("Goals");
        int cPeaks = Col("Peaks");

        // Minimal required
        if (cMapType < 0 || cSpawnX < 0 || cSpawnZ < 0)
            throw new Exception("CSV missing required columns. Required: MapType, SpawnX, SpawnZ.");

        for (int i = headerLineIndex + 1; i < lines.Length; i++)
        {
            string ln = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(ln)) continue;
            if (ln.StartsWith("#")) continue;

            var cells = SplitCsvLine(lines[i]);

            string Get(int col)
            {
                if (col < 0) return "";
                if (col >= cells.Count) return "";
                return cells[col];
            }

            int mapType = ParseMapTypeIndex(Get(cMapType));

            if (!TryParseFloat(Get(cSpawnX), out float sx) || !TryParseFloat(Get(cSpawnZ), out float sz))
            {
                Debug.LogWarning($"Skipping line {i + 1}: invalid spawn coords.");
                continue;
            }

            float cx = 0f, cz = 0f;
            if (TryParseFloat(Get(cCenterX), out float tmpX)) cx = tmpX;
            if (TryParseFloat(Get(cCenterZ), out float tmpZ)) cz = tmpZ;

            var goals = ParseVector2List(Get(cGoals));
            Vector2? goalOverride = (goals.Count > 0) ? goals[0] : (Vector2?)null;

            var peaks = ParsePeakList(Get(cPeaks));

            // If Multi-Peak, peaks are required
            if (mapType == 3 && (peaks == null || peaks.Count == 0))
            {
                Debug.LogWarning($"Skipping line {i + 1}: Multi-Peak requires Peaks.");
                continue;
            }

            trials.Add(new TrialSpec
            {
                MapTypeIndex = mapType,
                SpawnXZ = new Vector2(sx, sz),
                CenterXZ = new Vector2(cx, cz),
                GoalOverride = goalOverride,
                ExtraGoals = (goals.Count > 1) ? goals.Skip(1).ToList() : new List<Vector2>(),
                Peaks = peaks
            });
        }

        return trials;
    }

    private bool TryGetCsvPathFromSettings(out string csvPath)
    {
        csvPath = null;

        var setting = AppManager.Instance.Settings.SettingsList
            .OfType<EnumSetting>()
            .FirstOrDefault(s => s.Name == "Trial Source");

        if (setting == null || setting.Options == null || setting.Options.Count == 0)
            return false;

        int idx = Mathf.Clamp(setting.SelectedIndex, 0, setting.Options.Count - 1);
        string selected = setting.Options[idx];

        if (string.IsNullOrWhiteSpace(selected))
            return false;

        // Expect: "[CSV_NAME] (CSV)"
        if (!selected.EndsWith("(CSV)", StringComparison.OrdinalIgnoreCase))
            return false;

        string name = selected.Substring(0, selected.Length - "(CSV)".Length).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return false;

        csvPath = Path.Combine(AppManager.Instance.Settings.TrialsFolderPath, name + ".csv");
        return true;
    }

    private static List<string> SplitCsvLine(string line)
    {
        // Minimal CSV splitter: supports quoted fields with commas.
        var result = new List<string>();
        if (line == null) return result;

        bool inQuotes = false;
        var cur = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                // handle escaped quotes ""
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cur.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(cur.ToString().Trim());
                cur.Clear();
            }
            else
            {
                cur.Append(c);
            }
        }

        result.Add(cur.ToString().Trim());
        return result;
    }

    private static bool TryParseFloat(string s, out float f)
    {
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f);
    }

    private static bool TryParseVector2Pair(string s, out Vector2 v)
    {
        v = Vector2.zero;
        if (string.IsNullOrWhiteSpace(s)) return false;

        // Allow formats like "x z" or "x,z" inside the cell
        var cleaned = s.Replace(",", " ");
        var parts = cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        if (!TryParseFloat(parts[0], out float x)) return false;
        if (!TryParseFloat(parts[1], out float z)) return false;

        v = new Vector2(x, z);
        return true;
    }

    private static List<Vector2> ParseVector2List(string s)
    {
        var list = new List<Vector2>();
        if (string.IsNullOrWhiteSpace(s)) return list;

        // Strip outer quotes if any (SplitCsvLine already unquoted, but be safe)
        s = s.Trim().Trim('"');

        var items = s.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var item in items)
        {
            if (TryParseVector2Pair(item.Trim(), out var v))
                list.Add(v);
        }
        return list;
    }

    private static List<PeakSpec> ParsePeakList(string s)
    {
        var list = new List<PeakSpec>();
        if (string.IsNullOrWhiteSpace(s)) return list;

        s = s.Trim().Trim('"');

        var items = s.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var item in items)
        {
            var cleaned = item.Replace(",", " ").Trim();
            var parts = cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;

            if (!TryParseFloat(parts[0], out float x)) continue;
            if (!TryParseFloat(parts[1], out float z)) continue;
            if (!TryParseFloat(parts[2], out float a)) continue;

            list.Add(new PeakSpec(new Vector2(x, z), a));
        }
        return list;
    }

    private static int ParseMapTypeIndex(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return 0;

        s = s.Trim().Trim('"');

        // Allow numeric map type
        if (int.TryParse(s, out int idx))
            return Mathf.Clamp(idx, 0, StimulusManager.MapTypes.Count - 1);

        // Name match
        int found = StimulusManager.MapTypes.FindIndex(m => string.Equals(m, s, StringComparison.OrdinalIgnoreCase));
        return (found >= 0) ? found : 0;
    }

}
