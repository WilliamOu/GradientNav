using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Stores and manages all long-term settings

// Base setting class and subclasses
public abstract class SettingDef
{
    public string Name;
    public string Description;
}

public class BoolSetting : SettingDef
{
    public bool Value;
    public Action<bool> OnChanged;
}
public class FloatSetting : SettingDef
{
    public float Value;
    public float Min;
    public float Max;
    public Action<float> OnChanged;
}
public class IntegerSetting : SettingDef
{
    public int Value;
    public int Min;
    public int Max;
    public Action<int> OnChanged;
}
public class EnumSetting : SettingDef
{
    public int SelectedIndex;
    public List<string> Options;
    public Action<int> OnChanged;
}

// To add a new setting: 
// 1) Add a public field for the setting under "Cached public values" (see below)
// 2) Add a SettingDef for it in InitializeDefaultSettings()
// 3) Add a mapping line in UpdatedCachedValues()
public class SettingsManager
{
    private const string TrialsFolderName = "Trials";
    public string TrialsFolderPath => Path.Combine(Application.persistentDataPath, TrialsFolderName);

    // The "Database" of settings
    public List<SettingDef> SettingsList = new List<SettingDef>();

    // Cached public values
    // TODO: Add max and minimum intensity settings, and closeness check value (how close the current color has to be to the actual max brightness value)
    public float MouseSensitivity { get; private set; }
    public bool EnableMouseX { get; private set; }
    public bool EnableMouseY { get; private set; }
    public float MoveSpeed { get; private set; }
    public float MapLength { get; private set; }
    public float MapWidth { get; private set; }
    public float TimeToSeek { get; private set; }
    public float SuccessThreshold { get; private set; }
    public float DataLogInterval { get; private set; }
    public int BufferSizeBeforeWrite { get; private set; }
    public bool EnablePause { get; private set; }
    public bool ReorientAfterPause { get; private set; }
    public bool EnableSafetyWalls { get; private set; }
    public float SafetyWallRevealRadius { get; private set; }
    public float SafetyWallHandRevealRadius { get; private set; }
    public bool ExperimentalMode { get; private set; }
    public int ParticipantMaxTestCount { get; private set; }
    public int TrialCount { get; private set; }
    public float SigmaScale { get; private set; }
    public int MapTypeIndex { get; private set; }
    public int PeakCount { get; private set; }
    public int TrialSourceIndex { get; private set; }
    public int Seed { get; private set; }

    // TODO: Add a setting enabling or disabling VR movement via controller

    public SettingsManager()
    {
        InitializeDefaultSettings();
        LoadFromDisk();
    }

    public void InitializeDefaultSettings()
    {
        SettingsList = new List<SettingDef>();

        SettingsList.Add(new FloatSetting
        {
            Name = "Mouse Sensitivity",
            Description = "(Desktop Only) First person player controller mouse sensitivity.",
            Value = 1f,
            Min = float.MinValue,
            Max = float.MaxValue,
        });

        SettingsList.Add(new BoolSetting
        {
            Name = "Enable Mouse X",
            Description = "(Desktop Only) Allow the first person player controller to look side-to-side.",
            Value = true,
        });

        SettingsList.Add(new BoolSetting
        {
            Name = "Enable Mouse Y",
            Description = "(Desktop Only) Allow the first person player controller to look up and down.",
            Value = false,
        });

        SettingsList.Add(new FloatSetting
        {
            Name = "Move Speed",
            Description = "(Desktop Only) Use to match the speed of the first person player controller to the speed of VR participants.",
            Value = 2f,
            Min = 0f,
            Max = float.MaxValue,
        });

        SettingsList.Add(new FloatSetting
        {
            Name = "Map Length",
            Description = "Specified in meters. It is recommended to use a square play space.",
            Value = 10f,
            Min = 0f,
            Max = 9999f, // I'm going to assume nobody has a 9999x9999 meter testing space
        });

        SettingsList.Add(new FloatSetting
        {
            Name = "Map Width",
            Description = "Specified in meters. It is recommended to use a square play space.",
            Value = 10f,
            Min = 0f,
            Max = 9999f, // I'm going to assume nobody has a 9999x9999 meter testing space
        });

        SettingsList.Add(new FloatSetting
        {
            Name = "Time To Seek",
            Description = "The time (in seconds) allocated to the participant to find the brightest point.",
            Value = 30f,
            Min = 0f,
            Max = 9999f,
        });

        SettingsList.Add(new FloatSetting
        {
            Name = "Success Threshold",
            Description = "The brightness, as a percent of the maximum brightness, required for a test to succeeed.",
            Value = 0.9f,
            Min = 0f,
            Max = 1f,
        });

        SettingsList.Add(new FloatSetting
        {
            Name = "Data Log Interval",
            Description = "The time (in seconds) between recording the participant's 6DoF pose data to a buffer. A lower value may negatively impact performance.",
            Value = 0.011f,
            Min = 0.0001f,
            Max = 9999f,
        });

        SettingsList.Add(new IntegerSetting
        {
            Name = "Buffer Size Before Write",
            Description = "The number of data logs recorded to the buffer before writing to disk. Increase this value if the study begins stuttering.",
            Value = LogManager.MinBufferSize,
            Min = LogManager.MinBufferSize,
            Max = LogManager.MaxBufferSize,
        });

        SettingsList.Add(new BoolSetting
        {
            Name = "Enable Pause",
            Description = "Lets the participant pause the study by pressing either spacebar (desktop) or the selection input (VR).",
            Value = false,
        });

        SettingsList.Add(new BoolSetting
        {
            Name = "Reorient After Pause",
            Description = "(VR Only) Make the player walk back to where they were standing when they paused the trial via an orientation trial.",
            Value = false,
        });

        SettingsList.Add(new BoolSetting
        {
            Name = "Enable Safety Walls",
            Description = "(VR Only) Displays virtual walls when close to the map boundaries so the participant does not collide with a physical wall.",
            Value = true,
        });

        SettingsList.Add(new FloatSetting
        {
            Name = "Safety Wall Reveal Radius",
            Description = "How close the player's head has to be to the safety wall to reveal it.",
            Value = 1.0f,
            Min = 0.1f,
            Max = 9999f,
        });

        SettingsList.Add(new FloatSetting
        {
            Name = "Safety Wall Hand Reveal Radius",
            Description = "How close the player's controllers/hands have to be to the safety wall to reveal it.",
            Value = 0.6f,
            Min = 0.1f,
            Max = 9999f,
        });

        SettingsList.Add(new BoolSetting
        {
            Name = "Experimental Mode",
            Description = "Debugging. Allows you to see the map and position.",
            Value = false,
        });

        SettingsList.Add(new IntegerSetting
        {
            Name = "Participant Max Test Count",
            Description = "How many times the participant gets to check if they're at the point of maximum brightness. Use -1 for infinite.",
            Value = 1,
            Min = -1,
            Max = 9999,
        });

        SettingsList.Add(new IntegerSetting
        {
            Name = "Trial Count",
            Description = "The number of search trials.",
            Value = 3,
            Min = 1,
            Max = 9999,
        });

        SettingsList.Add(new FloatSetting
        {
            Name = "Sigma Scale",
            Description = "Sigma scaling parameter. Calculated as Min(Map Width, Map Length)/[scale parameter].",
            Value = 2f,
            Min = 0.0001f,
            Max = 9999f,
        });

        SettingsList.Add(new EnumSetting
        {
            Name = "Map Type",
            Description = "The type of map used when generating with the Random (no seed) or Random (seeded) source types.",
            SelectedIndex = 0,
            Options = StimulusManager.MapTypes,
        });

        SettingsList.Add(new IntegerSetting
        {
            Name = "Peak Count",
            Description = "The number of peaks to use when generating a Multi-Peak map with the Random (no seed) or Random (seeded) source types.",
            Value = 3,
            Min = 1,
            Max = 9999,
        });

        SettingsList.Add(new EnumSetting
        {
            Name = "Trial Source",
            Description = "How trials are defined: Random (no seed), Random (seeded), or load from a CSV in [PersistentDataPath]/Trials.",
            SelectedIndex = 0,
            Options = new List<string>(), // will be filled by RefreshTrialSourceOptions()
        });

        SettingsList.Add(new IntegerSetting
        {
            Name = "Seed",
            Description = "The seed used when generating maps with the Random (seeded) source type.",
            Value = 11111111,
            Min = 0,
            Max = int.MaxValue,
        });
    }

    public void UpdateCachedValues()
    {
        // This maps the "Stringly Typed" list to "Strongly Typed" fields
        MouseSensitivity = GetSetting<FloatSetting>("Mouse Sensitivity")?.Value ?? 1.0f;
        EnableMouseX = GetSetting<BoolSetting>("Enable Mouse X")?.Value ?? true;
        EnableMouseY = GetSetting<BoolSetting>("Enable Mouse Y")?.Value ?? false;
        MoveSpeed = GetSetting<FloatSetting>("Move Speed")?.Value ?? 2.0f;
        MapLength = GetSetting<FloatSetting>("Map Length")?.Value ?? 10.0f;
        MapWidth = GetSetting<FloatSetting>("Map Width")?.Value ?? 10.0f;
        TimeToSeek = GetSetting<FloatSetting>("Time To Seek")?.Value ?? 30.0f;
        SuccessThreshold = GetSetting<FloatSetting>("Success Threshold")?.Value ?? 0.9f;
        DataLogInterval = GetSetting<FloatSetting>("Data Log Interval")?.Value ?? 0.011f;
        BufferSizeBeforeWrite = GetSetting<IntegerSetting>("Buffer Size Before Write")?.Value ?? LogManager.MinBufferSize;
        EnablePause = GetSetting<BoolSetting>("Enable Pause")?.Value ?? false;
        ReorientAfterPause = GetSetting<BoolSetting>("Reorient After Pause")?.Value ?? false;
        EnableSafetyWalls = GetSetting<BoolSetting>("Enable Safety Walls")?.Value ?? true;
        SafetyWallRevealRadius = GetSetting<FloatSetting>("Safety Wall Reveal Radius")?.Value ?? 1.0f;
        SafetyWallHandRevealRadius = GetSetting<FloatSetting>("Safety Wall Hand Reveal Radius")?.Value ?? 0.6f;
        ExperimentalMode = GetSetting<BoolSetting>("Experimental Mode")?.Value ?? false;
        ParticipantMaxTestCount = GetSetting<IntegerSetting>("Participant Max Test Count")?.Value ?? 1;
        TrialCount = GetSetting<IntegerSetting>("Trial Count")?.Value ?? 3;
        SigmaScale = GetSetting<FloatSetting>("Sigma Scale")?.Value ?? 2.0f;
        MapTypeIndex = GetSetting<EnumSetting>("Map Type")?.SelectedIndex ?? 0;
        PeakCount = GetSetting<IntegerSetting>("Peak Count")?.Value ?? 3;
        TrialSourceIndex = GetSetting<EnumSetting>("Trial Source")?.SelectedIndex ?? 0;
        Seed = GetSetting<IntegerSetting>("Seed")?.Value ?? 11111111;

        // Debug.Log("Settings Cache Updated");
    }

    [Serializable]
    public class SettingsSaveData
    {
        // Use structs to pair Names with Values
        [Serializable]
        public struct BoolData { public string Name; public bool Value; }

        [Serializable]
        public struct FloatData { public string Name; public float Value; }

        [Serializable]
        public struct IntData { public string Name; public int Value; }

        [Serializable]
        public struct StringData { public string Name; public string Value; }

        // Separate lists for each type
        public List<BoolData> Bools = new List<BoolData>();
        public List<FloatData> Floats = new List<FloatData>();
        public List<IntData> Ints = new List<IntData>();
        public List<StringData> Strings = new List<StringData>();
    }

    public string FilePath => Path.Combine(Application.persistentDataPath, "settings.json");

    public void SaveToDisk()
    {
        // Create DTO
        SettingsSaveData saveData = new SettingsSaveData();

        foreach (var setting in SettingsList)
        {
            switch (setting)
            {
                case BoolSetting b:
                    saveData.Bools.Add(new SettingsSaveData.BoolData { Name = b.Name, Value = b.Value });
                    break;
                case FloatSetting n:
                    saveData.Floats.Add(new SettingsSaveData.FloatData { Name = n.Name, Value = n.Value });
                    break;
                case IntegerSetting i:
                    saveData.Ints.Add(new SettingsSaveData.IntData { Name = i.Name, Value = i.Value });
                    break;
                case EnumSetting e:
                    string selected = (e.Options != null && e.SelectedIndex >= 0 && e.SelectedIndex < e.Options.Count)
                        ? e.Options[e.SelectedIndex]
                        : "";
                    saveData.Strings.Add(new SettingsSaveData.StringData { Name = e.Name, Value = selected });

                    saveData.Ints.Add(new SettingsSaveData.IntData { Name = e.Name, Value = e.SelectedIndex });
                    break;
            }
        }

        // Serialize to JSON
        string json = JsonUtility.ToJson(saveData, true); // 'true' makes it pretty printed

        // Write to file
        try
        {
            File.WriteAllText(FilePath, json);
            // Debug.Log($"Settings saved to: {FilePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to save settings: {e.Message}");
        }

        UpdateCachedValues();
    }

    public void LoadFromDisk()
    {
        RefreshTrialSourceOptions();

        if (!File.Exists(FilePath))
        {
            SaveToDisk(); // Generates defaults
            Debug.Log($"No settings file exist. Generating one.");
        }

        try
        {
            // Read JSON
            string json = File.ReadAllText(FilePath);
            SettingsSaveData loadedData = JsonUtility.FromJson<SettingsSaveData>(json);
            if (loadedData == null)
            {
                Debug.LogError("Failed to parse settings.json, regenerating defaults.");
                SaveToDisk();
                return;
            }

            // Apply values to the Runtime List
            // Loop through the LOADED data and find the matching Runtime setting

            if (loadedData.Bools != null)
            {
                // Apply Bools
                foreach (var boolData in loadedData.Bools)
                {
                    var setting = SettingsList.OfType<BoolSetting>().FirstOrDefault(s => s.Name == boolData.Name);
                    if (setting != null)
                    {
                        setting.Value = boolData.Value;
                        // Manually invoke the callback so the app reacts to the loaded value
                        setting.OnChanged?.Invoke(setting.Value);
                    }
                }
            }   

            if (loadedData.Floats != null)
            {
                // Apply Floats
                foreach (var floatData in loadedData.Floats)
                {
                    var setting = SettingsList.OfType<FloatSetting>().FirstOrDefault(s => s.Name == floatData.Name);
                    if (setting != null)
                    {
                        // Safety: Clamp again just to be safe in case JSON was edited manually
                        setting.Value = Mathf.Clamp(floatData.Value, setting.Min, setting.Max);
                        setting.OnChanged?.Invoke(setting.Value);
                    }
                }
            }


            if (loadedData.Strings != null)
            {
                // Apply Strings (prefer this for EnumSetting to avoid index drift)
                foreach (var stringData in loadedData.Strings)
                {
                    var settingDef = SettingsList.FirstOrDefault(s => s.Name == stringData.Name);
                    if (settingDef is EnumSetting enumSetting)
                    {
                        int idx = enumSetting.Options?.FindIndex(o => string.Equals(o, stringData.Value, StringComparison.OrdinalIgnoreCase)) ?? -1;

                        enumSetting.SelectedIndex = (idx >= 0) ? idx : 0;
                        enumSetting.OnChanged?.Invoke(enumSetting.SelectedIndex);
                    }
                }
            }

            if (loadedData.Ints != null)
            {
                // Apply Ints (Handles both IntegerSetting and EnumSetting)
                foreach (var intData in loadedData.Ints)
                {
                    // Find ANY setting with this name
                    var settingDef = SettingsList.FirstOrDefault(s => s.Name == intData.Name);

                    if (settingDef is IntegerSetting intSetting)
                    {
                        intSetting.Value = Mathf.Clamp(intData.Value, intSetting.Min, intSetting.Max);
                        intSetting.OnChanged?.Invoke(intSetting.Value);
                    }
                    else if (settingDef is EnumSetting enumSetting)
                    {
                        // If we already had a string entry for this enum, do not overwrite it with int fallback.
                        bool hasString = loadedData.Strings != null && loadedData.Strings.Any(s => s.Name == intData.Name);
                        if (!hasString)
                        {
                            int max = (enumSetting.Options != null && enumSetting.Options.Count > 0) ? enumSetting.Options.Count - 1 : 0;
                            enumSetting.SelectedIndex = Mathf.Clamp(intData.Value, 0, max);
                            enumSetting.OnChanged?.Invoke(enumSetting.SelectedIndex);
                        }
                    }
                }
            }

            // Debug.Log("Settings loaded successfully.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load settings: {e.Message}");
        }

        UpdateCachedValues();
    }

    // Helper to grab values
    // Brute force, but there aren't that many settings so performance shouldn't be an issue
    private T GetSetting<T>(string name) where T : SettingDef
    {
        var s = SettingsList.OfType<T>().FirstOrDefault(x => x.Name == name);
        if (s == null)
        {
            // This log will tell you exactly which setting is misspelled
            Debug.LogError($"CRITICAL: Setting '{name}' of type {typeof(T).Name} not found in SettingsList!");
            return null;
        }
        return s;
    }

    private List<string> BuildTrialSourceOptions()
    {
        Directory.CreateDirectory(TrialsFolderPath);

        var options = new List<string> { "Random (no seed)", "Random (seeded)" };

        try
        {
            var csvFiles = Directory.GetFiles(TrialsFolderPath, "*.csv", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Select(n => $"{n} (CSV)");

            options.AddRange(csvFiles);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Could not enumerate Trials CSVs: {ex.Message}");
        }

        return options;
    }


    public void RefreshTrialSourceOptions()
    {
        var setting = GetSetting<EnumSetting>("Trial Source");
        if (setting == null) return;

        string previouslySelected =
            (setting.Options != null &&
             setting.SelectedIndex >= 0 &&
             setting.SelectedIndex < setting.Options.Count)
                ? setting.Options[setting.SelectedIndex]
                : null;

        var newOptions = BuildTrialSourceOptions();
        setting.Options = newOptions;

        int newIndex = 0;
        if (!string.IsNullOrEmpty(previouslySelected))
        {
            int found = newOptions.FindIndex(o => string.Equals(o, previouslySelected, StringComparison.OrdinalIgnoreCase));
            if (found >= 0) newIndex = found;
        }

        int oldIndex = setting.SelectedIndex;
        setting.SelectedIndex = Mathf.Clamp(newIndex, 0, setting.Options.Count - 1);

        if (setting.SelectedIndex != oldIndex)
            setting.OnChanged?.Invoke(setting.SelectedIndex);
    }
}