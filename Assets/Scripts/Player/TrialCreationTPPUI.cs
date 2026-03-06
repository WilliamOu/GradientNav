using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TrialCreationTPPUI : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private ThreePerspectivePlayer playerController;

    [Header("File Selection")]
    [SerializeField] private TMP_Dropdown fileDropdown;
    [SerializeField] private TMP_InputField newFileNameInput;
    [SerializeField] private Button createNewFileBtn;
    [SerializeField] private Button dupeFileBtn;
    [SerializeField] private Button saveFileBtn;
    [SerializeField] private Button backBtn;
    [SerializeField] private Button renameBtn;
    [SerializeField] private GameObject panel;
    [SerializeField] private Transform outOfBoundsLocation;

    [Header("Trial Navigation")]
    [SerializeField] private TMP_Dropdown trialSelectorDropdown;
    [SerializeField] private Button addTrialBtn;
    [SerializeField] private Button removeTrialBtn;
    [SerializeField] private Button duplicateTrialBtn;

    [Header("Trial Data Editing")]
    [SerializeField] private TMP_Dropdown mapTypeDropdown;
    [SerializeField] private TMP_InputField sigmaInput;
    [SerializeField] private TMP_InputField spawnInput;   // "x, y"
    [SerializeField] private TMP_InputField goalInput;    // "x, y"

    [Header("Map Specifics")]
    [SerializeField] private GameObject centerContainer;  // For Gaussian/Inverse/Torus
    [SerializeField] private TMP_InputField centerInput;  // "x, y"

    [SerializeField] private GameObject multiPeakContainer; // For Multi-Peak
    [SerializeField] private TMP_InputField peaksInput;   // "x, y, amp | ..."
    [SerializeField] private TMP_InputField peakCountGenInput; // For random gen
    [SerializeField] private Button generatePeaksBtn;

    [SerializeField] private GameObject matrixContainer;
    [SerializeField] private TMP_InputField mapFileNameInput;

    [Header("Visualization")]
    [SerializeField] private GameObject OrientPillar;
    private LineRenderer spawnLine;

    [Header("Settings")]
    [SerializeField] private Button toggleMapBoundsBtn;
    private bool mapBoundsToggled = false;

    // Internal State
    private List<TrialSpec> currentTrials = new List<TrialSpec>();
    private string currentFilePath;
    private int activeTrialIndex = -1;
    private bool uiIsUpdating = false; // Prevent infinite loops

    // Only used for matrix evolution
    private float _nextRefreshTime = 0f;

    private void OnEnable()
    {
        if (playerController != null)
        {
            playerController.OnFreezeStateChanged += HandleFreeze;
        }
    }

    private void OnDisable()
    {
        if (playerController != null)
        {
            playerController.OnFreezeStateChanged -= HandleFreeze;
        }
    }

    private void Start()
    {
        if (panel != null) panel.SetActive(true);

        RefreshFileList();

        // Setup Listeners
        fileDropdown.onValueChanged.AddListener(OnAvailableFileSelected);
        trialSelectorDropdown.onValueChanged.AddListener(OnTrialSelected);

        // Buttons
        saveFileBtn.onClick.AddListener(SaveAndReturn);
        dupeFileBtn.onClick.AddListener(DuplicateCurrentFile);
        backBtn.onClick.AddListener(ReturnToTitle);
        createNewFileBtn.onClick.AddListener(CreateNewFile);
        addTrialBtn.onClick.AddListener(AddNewTrial);
        removeTrialBtn.onClick.AddListener(RemoveCurrentTrial);
        duplicateTrialBtn.onClick.AddListener(DuplicateCurrentTrial);
        renameBtn.onClick.AddListener(() => { RenameCurrentFile(newFileNameInput.text); });
        generatePeaksBtn.onClick.AddListener(GenerateRandomPeaksForCurrent);

        // Data Inputs (Auto-update map on deselect/submit)
        mapTypeDropdown.onValueChanged.AddListener(_ => PushUiToData());
        sigmaInput.onEndEdit.AddListener(_ => PushUiToData());
        spawnInput.onEndEdit.AddListener(_ => PushUiToData());
        goalInput.onEndEdit.AddListener(_ => PushUiToData());
        centerInput.onEndEdit.AddListener(_ => PushUiToData());
        peaksInput.onEndEdit.AddListener(_ => PushUiToData());

        toggleMapBoundsBtn.onClick.AddListener(ToggleMapBounds);

        if (mapFileNameInput != null) mapFileNameInput.onEndEdit.AddListener(_ => StartCoroutine(DelayedPushUiToData()));

        if (playerController != null)
        {
            HandleFreeze(playerController.IsFrozen);
        }
        // if (panel != null) panel.SetActive(false);
        AppManager.Instance.Utilities.MapVisualizer.ToggleMap(true);
        // AppManager.Instance.Utilities.Minimap.ManualUpdate(outOfBoundsLocation);
        AppManager.Instance.Utilities.Minimap.gameObject.SetActive(true);
    }

    // Rip performance
    private void Update()
    {
        if (!AppManager.Instance.Settings.RefreshMatrixEvolution) return;
        if (AppManager.Instance.Session.MapType != "Matrix") return;

        float refreshInterval = AppManager.Instance.Session.TimeEvolutionSpeed; 
        if (Time.time < _nextRefreshTime) return;
        _nextRefreshTime = Time.time + refreshInterval;

        AppManager.Instance.Utilities.Minimap.RefreshMinimapFast();
        AppManager.Instance.Utilities.MapVisualizer.UpdateMeshValuesOnly();
    }

    private void OnDestroy()
    {
        if (playerController != null) playerController.OnFreezeStateChanged -= HandleFreeze;
    }

    // UI Visibility Logic
    private void HandleFreeze(bool isFrozen)
    {
        if (panel == null) return;

        panel.SetActive(isFrozen);

        if (!isFrozen)
        {
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }
    }

    // File Loading
    private void RefreshFileList()
    {
        // Use your existing logic, or simple Directory.GetFiles
        fileDropdown.ClearOptions();
        string path = AppManager.Instance.Settings.TrialsFolderPath;
        Directory.CreateDirectory(path);

        var files = Directory.GetFiles(path, "*.csv").Select(Path.GetFileName).ToList();
        fileDropdown.AddOptions(files);

        if (files.Count > 0)
        {
            fileDropdown.value = 0;
            LoadFile(Path.Combine(path, files[0]));
        }
    }

    private void OnAvailableFileSelected(int index)
    {
        string fileName = fileDropdown.options[index].text;
        string path = Path.Combine(AppManager.Instance.Settings.TrialsFolderPath, fileName);
        LoadFile(path);
    }

    private void OnTrialSelected(int index)
    {
        // Just a wrapper to match the UnityEvent signature
        SelectTrial(index);
    }

    private void ClearUI()
    {
        // Reset all inputs to blank so we don't show stale data
        mapTypeDropdown.value = 0;
        sigmaInput.text = "";
        spawnInput.text = "";
        goalInput.text = "";
        centerInput.text = "";
        peaksInput.text = "";

        // Hide sub-menus
        if (centerContainer != null) centerContainer.SetActive(false);
        if (multiPeakContainer != null) multiPeakContainer.SetActive(false);
        if (mapFileNameInput != null) mapFileNameInput.text = "";
        if (matrixContainer != null) matrixContainer.SetActive(false);
    }

    private void LoadFile(string path)
    {
        currentFilePath = path;
        currentTrials = TrialManager.LoadTrialsFromCsv(path);
        RefreshTrialDropdown();
    }

    private void CreateNewFile()
    {
        string folder = AppManager.Instance.Settings.TrialsFolderPath;
        string inputName = newFileNameInput.text.Trim();

        if (string.IsNullOrEmpty(inputName)) inputName = "NewFile";

        currentFilePath = GetUniqueFilePath(folder, inputName);
        currentTrials = new List<TrialSpec> { new TrialSpec() };

        SaveCurrentFile();
        RefreshFileList();

        string newFileName = Path.GetFileName(currentFilePath);
        int index = fileDropdown.options.FindIndex(opt => opt.text == newFileName);
        if (index >= 0)
        {
            fileDropdown.value = index;
            LoadFile(currentFilePath);
        }
    }

    public void DuplicateCurrentFile()
    {
        if (string.IsNullOrEmpty(currentFilePath)) return;

        string folder = AppManager.Instance.Settings.TrialsFolderPath;
        string currentName = Path.GetFileNameWithoutExtension(currentFilePath);

        string newPath = GetUniqueFilePath(folder, currentName);

        TrialManager.SaveTrialsToCsv(newPath, currentTrials);
        currentFilePath = newPath;
        RefreshFileList();

        string newFileName = Path.GetFileName(newPath);
        int index = fileDropdown.options.FindIndex(opt => opt.text == newFileName);
        if (index >= 0)
        {
            fileDropdown.value = index;
            LoadFile(currentFilePath);
        }
    }

    private void RenameCurrentFile(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || string.IsNullOrEmpty(currentFilePath)) return;

        // Strip and re-add extension just in case they typed it manually
        newName = newName.Replace(".csv", "") + ".csv";

        string folder = AppManager.Instance.Settings.TrialsFolderPath;
        string newPath = Path.Combine(folder, newName);

        if (currentFilePath == newPath) return; // No change

        if (File.Exists(newPath))
        {
            Debug.LogWarning($"Cannot rename: A file named {newName} already exists.");
            return;
        }

        SaveCurrentFile(); // Save latest changes before moving
        File.Move(currentFilePath, newPath);
        currentFilePath = newPath;

        // Note: This automatically loads files[0] in the background
        RefreshFileList();

        // ync the dropdown AND force the backend to reload our newly renamed file
        int index = fileDropdown.options.FindIndex(opt => opt.text == newName);
        if (index >= 0)
        {
            fileDropdown.value = index; // Updates the UI
            LoadFile(currentFilePath);  // Overrides the files[0] load from RefreshFileList
        }
    }

    private void SaveCurrentFile()
    {
        if (string.IsNullOrEmpty(currentFilePath))
        {
            Debug.LogWarning("Cannot save: No file path selected.");
            return;
        }

        if (string.IsNullOrEmpty(currentFilePath)) return;
        TrialManager.SaveTrialsToCsv(currentFilePath, currentTrials);
    }

    private void ReturnToTitle()
    {
        AppManager.Instance.Utilities.MapVisualizer.ToggleMap(false);
        AppManager.Instance.Utilities.Minimap.ToggleMinimap(false);
        SceneManager.LoadScene("Title Scene");
    }

    private void SaveAndReturn()
    {
        SaveCurrentFile();
        ReturnToTitle();
    }

    private void ToggleMapBounds()
    {
        mapBoundsToggled = !mapBoundsToggled;
        if (mapBoundsToggled)
        {
            AppManager.Instance.Utilities.MapVisualizer.UpdateMeshGeometry();
        }
        else
        {
            float mapWidth = Mathf.Max(100, AppManager.Instance.Settings.MapWidth * 10);
            float mapLength = Mathf.Max(100, AppManager.Instance.Settings.MapLength * 10);
            AppManager.Instance.Utilities.MapVisualizer.UpdateMeshGeometry(mapWidth, mapLength);
        }
    }

    // Trial Management
    private void RefreshTrialDropdown()
    {
        trialSelectorDropdown.ClearOptions();
        var options = new List<string>();
        for (int i = 0; i < currentTrials.Count; i++)
            options.Add($"Trial {i + 1} ({StimulusManager.MapTypes[currentTrials[i].MapTypeIndex]})");

        trialSelectorDropdown.AddOptions(options);

        if (currentTrials.Count > 0)
            SelectTrial(0);
        else
        {
            activeTrialIndex = -1;
            ClearUI();
        }
    }

    private void SelectTrial(int index)
    {
        if (index < 0 || index >= currentTrials.Count) return;
        activeTrialIndex = index;
        trialSelectorDropdown.SetValueWithoutNotify(index);
        PopulateUI(currentTrials[index]);
        Update3DPreview();
    }

    private void AddNewTrial()
    {
        currentTrials.Add(new TrialSpec { MapTypeIndex = 0, Peaks = new List<PeakSpec>() });
        RefreshTrialDropdown();
        SelectTrial(currentTrials.Count - 1);
    }

    private void RemoveCurrentTrial()
    {
        if (activeTrialIndex < 0) return;
        currentTrials.RemoveAt(activeTrialIndex);
        RefreshTrialDropdown();
    }

    private void DuplicateCurrentTrial()
    {
        if (activeTrialIndex < 0) return;
        // Simple clone (JSON is a lazy way to deep copy, or manual copy)
        var json = JsonUtility.ToJson(currentTrials[activeTrialIndex]);
        var clone = JsonUtility.FromJson<TrialSpec>(json);
        // Note: Peak list might need manual deep copy if using classes, structs are fine

        currentTrials.Add(clone);
        RefreshTrialDropdown();
        SelectTrial(currentTrials.Count - 1);
    }

    // Data Binding (Spec <-> UI)
    private void PopulateUI(TrialSpec spec)
    {
        uiIsUpdating = true;

        mapTypeDropdown.ClearOptions();
        mapTypeDropdown.AddOptions(StimulusManager.MapTypes);
        mapTypeDropdown.value = spec.MapTypeIndex;

        if (mapFileNameInput != null) mapFileNameInput.text = spec.MapFileName ?? "";

        if (spec.MapTypeIndex == 5)
        {
            // Show list like: "0.50 | 0.80 | 1.20"
            if (spec.SigmaOverrides != null && spec.SigmaOverrides.Count > 0)
                sigmaInput.text = string.Join(" | ", spec.SigmaOverrides.Select(v => v.ToString()));
            else
                sigmaInput.text = "";
        }
        else
        {
            sigmaInput.text = spec.SigmaOverride.HasValue ? spec.SigmaOverride.Value.ToString() : "";
        }
        spawnInput.text = $"{spec.SpawnXZ.x}, {spec.SpawnXZ.y}";
        goalInput.text = spec.GoalOverride.HasValue ? $"{spec.GoalOverride.Value.x}, {spec.GoalOverride.Value.y}" : "";
        centerInput.text = $"{spec.CenterXZ.x}, {spec.CenterXZ.y}";

        // Handle Peaks List -> String
        if (spec.Peaks != null)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < spec.Peaks.Count; i++)
            {
                var p = spec.Peaks[i];
                sb.Append($"{p.Position.x},{p.Position.y},{p.Amplitude}");
                if (i < spec.Peaks.Count - 1) sb.Append(" | ");
            }
            peaksInput.text = sb.ToString();
        }
        else peaksInput.text = "";

        // Container Visibility
        bool isMultiPeak = (spec.MapTypeIndex == 3 || spec.MapTypeIndex == 5);
        bool isMatrix = (spec.MapTypeIndex == 6);

        if (multiPeakContainer != null) multiPeakContainer.SetActive(isMultiPeak);
        if (centerContainer != null) centerContainer.SetActive(!isMultiPeak && !isMatrix);
        if (matrixContainer != null) matrixContainer.SetActive(isMatrix);

        // Enable peak randomize only for multi-peak
        if (generatePeaksBtn != null) generatePeaksBtn.interactable = isMultiPeak;
        if (peaksInput != null) peaksInput.interactable = isMultiPeak;
        if (peakCountGenInput != null) peakCountGenInput.interactable = isMultiPeak;

        uiIsUpdating = false;
    }

    private IEnumerator DelayedPushUiToData()
    {
        yield return null;
        PushUiToData();
    }

    public void PushUiToData()
    {
        if (uiIsUpdating || activeTrialIndex < 0) return;

        var spec = currentTrials[activeTrialIndex];

        // Map Type
        spec.MapTypeIndex = mapTypeDropdown.value;

        // Vector Parsers
        spec.SpawnXZ = ParseVector2(spawnInput.text);
        spec.CenterXZ = ParseVector2(centerInput.text);

        Vector2 g = ParseVector2(goalInput.text);
        if (g == Vector2.zero && string.IsNullOrWhiteSpace(goalInput.text)) spec.GoalOverride = null;
        else spec.GoalOverride = g;

        // Sigma / Sigmas
        if (spec.MapTypeIndex == 5)
        {
            spec.SigmaOverrides = TrialManager.ParseFloatList(sigmaInput.text);

            // Optional: don’t use single sigma for this type
            spec.SigmaOverride = null;
        }
        else
        {
            if (float.TryParse(sigmaInput.text, out float sVal)) spec.SigmaOverride = sVal;
            else spec.SigmaOverride = null;

            // Optional: keep list empty for non-type-5 to avoid confusion
            if (spec.SigmaOverrides == null) spec.SigmaOverrides = new List<float>();
            else spec.SigmaOverrides.Clear();
        }

        // Peaks Parsing
        if (spec.MapTypeIndex == 3 || spec.MapTypeIndex == 5) // MultiPeak + Linear Multi-Peak
        {
            spec.Peaks = new List<PeakSpec>();
            var entries = peaksInput.text.Split('|');
            foreach (var entry in entries)
            {
                var parts = entry.Split(',');
                if (parts.Length >= 2)
                {
                    float x = float.Parse(parts[0]);
                    float y = float.Parse(parts[1]);
                    float amp = (parts.Length > 2) ? float.Parse(parts[2]) : 1.0f;
                    spec.Peaks.Add(new PeakSpec(new Vector2(x, y), amp));
                }
            }
        }

        // Matrix Parsing
        if (mapFileNameInput != null)
        {
            if (spec.MapTypeIndex == 6)
                spec.MapFileName = string.IsNullOrWhiteSpace(mapFileNameInput.text) ? null : mapFileNameInput.text.Trim();
            else
                spec.MapFileName = null;
        }

        // Update Name in dropdown
        var options = trialSelectorDropdown.options;
        options[activeTrialIndex].text = $"Trial {activeTrialIndex + 1} ({StimulusManager.MapTypes[spec.MapTypeIndex]})";
        trialSelectorDropdown.RefreshShownValue();

        bool isMultiPeak = (spec.MapTypeIndex == 3 || spec.MapTypeIndex == 5);
        bool isMatrix = (spec.MapTypeIndex == 6);

        if (multiPeakContainer != null) multiPeakContainer.SetActive(isMultiPeak);
        if (centerContainer != null) centerContainer.SetActive(!isMultiPeak && !isMatrix);
        if (matrixContainer != null) matrixContainer.SetActive(isMatrix);

        if (generatePeaksBtn != null) generatePeaksBtn.interactable = isMultiPeak;
        if (peaksInput != null) peaksInput.interactable = isMultiPeak;
        if (peakCountGenInput != null) peakCountGenInput.interactable = isMultiPeak;

        Update3DPreview();
    }

    private void GenerateRandomPeaksForCurrent()
    {
        if (!int.TryParse(peakCountGenInput.text, out int count)) count = 3;

        // Use your Factory!
        float radius = Mathf.Min(AppManager.Instance.Settings.MapWidth, AppManager.Instance.Settings.MapLength) / 2f;
        int seed = UnityEngine.Random.Range(0, 10000);

        var peaks = MultiPeakSpecFactory.Create(seed, radius, count);

        // Apply to current
        currentTrials[activeTrialIndex].Peaks = peaks;

        // Refresh UI to show the new string
        PopulateUI(currentTrials[activeTrialIndex]);
        Update3DPreview();
    }

    // Visualization
    private void Update3DPreview()
    {
        if (activeTrialIndex < 0) return;

        var spec = currentTrials[activeTrialIndex];

        // Important: We must inject this spec into the StimulusManager 
        // so the visualizer (which calls GetIntensity) sees the NEW map.

        float width = AppManager.Instance.Settings.MapWidth;
        float length = AppManager.Instance.Settings.MapLength;
        Vector2 centerOffset = spec.CenterXZ;

        // Force the Stimulus Manager to generate this map NOW
        AppManager.Instance.Stimulus.GenerateMap(
            spec.MapTypeIndex,
            width,
            length,
            centerOffset,
            spec.GoalOverride,
            spec.Peaks,
            sigmaOverride: spec.SigmaOverride,
            sigmaOverrides: spec.SigmaOverrides,
            mapFileName: spec.MapFileName
        );

        AppManager.Instance.Session.MapType = StimulusManager.MapTypes[spec.MapTypeIndex];

        // Now tell the visualizer to redraw the mesh
        AppManager.Instance.Utilities.MapVisualizer.UpdateMeshGeometry();
        AppManager.Instance.Utilities.Minimap.RefreshMinimap();

        UpdateSpawnVisualization(spec.SpawnXZ);
    }

    private void UpdateSpawnVisualization(Vector2 spawnXZ)
    {
        if (OrientPillar != null)
        {
            // Move pillar to XZ
            OrientPillar.transform.position = new Vector3(spawnXZ.x, AppManager.Instance.Utilities.MapVisualizer.heightMultiplier * AppManager.Instance.Stimulus.GetIntensity(spawnXZ), spawnXZ.y);

            // Ensure LineRenderer exists
            if (spawnLine == null)
            {
                spawnLine = OrientPillar.GetComponent<LineRenderer>();
                if (spawnLine == null)
                {
                    spawnLine = OrientPillar.AddComponent<LineRenderer>();
                    spawnLine.startWidth = 0.1f;
                    spawnLine.endWidth = 0.1f;
                    spawnLine.positionCount = 2;
                    spawnLine.material = new Material(Shader.Find("Sprites/Default"));
                    spawnLine.startColor = Color.blue;
                    spawnLine.endColor = Color.cyan;
                }
            }

            // Beam line straight up from 0 to 1000 on the Y axis
            spawnLine.SetPosition(0, new Vector3(spawnXZ.x, 0, spawnXZ.y));
            spawnLine.SetPosition(1, new Vector3(spawnXZ.x, 1000f, spawnXZ.y));
            AppManager.Instance.Utilities.Minimap.UpdatePlayerIconExternal(spawnXZ);
        }
    }

    // Helper
    private Vector2 ParseVector2(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Vector2.zero;
        var parts = s.Split(',');
        if (parts.Length < 2) parts = s.Split(' '); // try space
        if (parts.Length < 2) return Vector2.zero;

        float.TryParse(parts[0], out float x);
        float.TryParse(parts[1], out float y);
        return new Vector2(x, y);
    }

    private string GetUniqueFilePath(string folder, string currentFileNameWithoutExt)
    {
        var regex = new Regex(@"^(.*) \((\d+)\)$");

        var match = regex.Match(currentFileNameWithoutExt);

        string baseName = currentFileNameWithoutExt;
        int nextIndex = 1;
        if (match.Success)
        {
            baseName = match.Groups[1].Value;
            string numberStr = match.Groups[2].Value;

            if (int.TryParse(numberStr, out int currentNum))
            {
                nextIndex = currentNum + 1;
            }
        }

        while (true)
        {
            string candidateName = $"{baseName} ({nextIndex})";
            string fullPath = Path.Combine(folder, candidateName + ".csv");

            if (!File.Exists(fullPath))
            {
                return fullPath;
            }

            nextIndex++;
        }
    }
}