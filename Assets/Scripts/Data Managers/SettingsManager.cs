using UnityEngine;
using System;
using UnityEngine.UIElements;
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
// 2) Add a SettingDef for it in InitializeDefaultValues()
// 3) Add a mapping line in UpdatedCachedValues()
public class SettingsManager
{
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
    public bool EnableSafetyWalls { get; private set; } // TODO: Implement
    public bool ExperimentalMode { get; private set; }
    public int ParticipantMaxTestCount { get; private set; }
    public int GaussianTypeIndex { get; private set; }

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
            Value = 8f,
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
            Name = "Enable Safety Walls",
            Description = "(VR Only) Displays virtual walls when close to the map boundaries so the participant does not collide with a physical wall.",
            Value = true,
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

        SettingsList.Add(new EnumSetting
        {
            Name = "Gaussian Type",
            Description = "Guassian Function Type.",
            SelectedIndex = 0,
            Options = PlayerManager.MapTypes,
        });
    }

    public void UpdateCachedValues()
    {
        // This maps the "Stringly Typed" list to "Strongly Typed" fields
        MouseSensitivity = GetSetting<FloatSetting>("Mouse Sensitivity")?.Value ?? 1.0f;
        EnableMouseX = GetSetting<BoolSetting>("Enable Mouse X")?.Value ?? true;
        EnableMouseY = GetSetting<BoolSetting>("Enable Mouse Y")?.Value ?? false;
        MoveSpeed = GetSetting<FloatSetting>("Move Speed")?.Value ?? 8.0f;
        MapLength = GetSetting<FloatSetting>("Map Length")?.Value ?? 1.0f;
        MapWidth = GetSetting<FloatSetting>("Map Width")?.Value ?? 10.0f;
        TimeToSeek = GetSetting<FloatSetting>("Time To Seek")?.Value ?? 30.0f;
        SuccessThreshold = GetSetting<FloatSetting>("Success Threshold")?.Value ?? 0.9f;
        DataLogInterval = GetSetting<FloatSetting>("Data Log Interval")?.Value ?? 0.011f;
        BufferSizeBeforeWrite = GetSetting<IntegerSetting>("Buffer Size Before Write")?.Value ?? LogManager.MinBufferSize;
        EnableSafetyWalls = GetSetting<BoolSetting>("Enable Safety Walls")?.Value ?? true;
        ExperimentalMode = GetSetting<BoolSetting>("Experimental Mode")?.Value ?? false;
        ParticipantMaxTestCount = GetSetting<IntegerSetting>("Participant Max Test Count")?.Value ?? 1;
        GaussianTypeIndex = GetSetting<EnumSetting>("Gaussian Type")?.SelectedIndex ?? 0;

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

        // Separate lists for each type
        public List<BoolData> Bools = new List<BoolData>();
        public List<FloatData> Floats = new List<FloatData>();
        public List<IntData> Ints = new List<IntData>();
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

            // Apply values to the Runtime List
            // Loop through the LOADED data and find the matching Runtime setting

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
                    enumSetting.SelectedIndex = Mathf.Clamp(intData.Value, 0, enumSetting.Options.Count - 1);
                    enumSetting.OnChanged?.Invoke(enumSetting.SelectedIndex);
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
}