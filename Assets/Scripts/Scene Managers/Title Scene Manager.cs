using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum MenuState
{
    Main,
    Settings
}

public class TitleSceneManager : MonoBehaviour
{
    private const string GameSceneName = "Gradient Navigation Scene";

    [SerializeField] private UnityEngine.UI.Button ExitButton;
    [SerializeField] private GameObject MainMenu;
    [SerializeField] private GameObject SettingsMenu;
    [SerializeField] private TMP_Text MainMenuErrorMessageTextField;
    [SerializeField] private TMP_Text SettingsMenuErrorMessageTextField;
    private Coroutine clearErrorCoroutine;
    private MenuState menuState;

    // Main Menu Elements
    [SerializeField] private TMP_InputField Participant;
    [SerializeField] private TMP_InputField Gender;
    [SerializeField] private TMP_InputField Date;
    [SerializeField] private UnityEngine.UI.Button FileLocationButton;
    [SerializeField] private UnityEngine.UI.Button SettingsButton;

    // Settings Menu Elements
    [SerializeField] private UnityEngine.UI.Button SaveButton;
    [SerializeField] private UnityEngine.UI.Button BackButton;
    [SerializeField] private Transform settingsContentContainer;
    [SerializeField] private GameObject settingRowPrefab;
    [SerializeField] private UnityEngine.UI.Button VRStartButton;
    [SerializeField] private UnityEngine.UI.Button DesktopStartButton;

    private void Awake()
    {
        // To prevent race conditions with the master app manager loading in Awake, this should never be touched
    }
    void Start()
    {
        // So GameObjects are deterministically active during the initialization
        MainMenu.SetActive(true);
        SettingsMenu.SetActive(true);

        // Initialize Listeners
        ExitButton.onClick.AddListener(CloseApplication);
        FileLocationButton.onClick.AddListener(OpenPersistentDataPath);

        SettingsButton.onClick.AddListener(() =>
        {
            AppManager.Instance.Settings.LoadFromDisk();
            GenerateSettingsUI();

            MainMenu.SetActive(false);
            SettingsMenu.SetActive(true);
            menuState = MenuState.Settings;
        });

        SaveButton.onClick.AddListener(() =>
        {
            AppManager.Instance.Settings.SaveToDisk();
            MainMenu.SetActive(true);
            SettingsMenu.SetActive(false);
            menuState = MenuState.Main;
            WriteMessage("Settings Saved", Color.white, 2);
        });

        BackButton.onClick.AddListener(() =>
        {
            AppManager.Instance.Settings.LoadFromDisk();
            MainMenu.SetActive(true);
            SettingsMenu.SetActive(false);
            menuState = MenuState.Main;
            WriteMessage("Changes Discarded", Color.white, 2);
        });

        DesktopStartButton.onClick.AddListener(() =>
        {
            UnityEngine.Debug.Log("Loading Desktop");
            ParseSessionDataAndLoad(SessionDataManager.GameMode.Default, SessionDataManager.SessionType.Desktop);
        });

        VRStartButton.onClick.AddListener(() =>
        {
            UnityEngine.Debug.Log("Loading VR");
            ParseSessionDataAndLoad(SessionDataManager.GameMode.Default, SessionDataManager.SessionType.VR);
        });

        // Initial menu state
        MainMenu.SetActive(true);
        SettingsMenu.SetActive(false);
        menuState = MenuState.Main;
    }

    private bool TryParseGender(string input, out SessionDataManager.Gender gender)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            gender = SessionDataManager.Gender.Unspecified;
            return true;
        }

        // Allows "Non-Binary", "Non Binary" to map to NonBinary
        string normalized = new string(input.Where(char.IsLetterOrDigit).ToArray());

        return Enum.TryParse(normalized, true, out gender);
    }

    private bool TryParseDateMMDDYYYY(string input, out string normalizedDate)
    {
        normalizedDate = null;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        // Strictly require exactly "MM/dd/yyyy"
        if (!DateTime.TryParseExact(
                input.Trim(),
                "MM/dd/yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dt))
        {
            return false;
        }

        // Store a normalized version (ensures leading zeros, etc.)
        normalizedDate = dt.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
        return true;
    }

    private void ParseSessionDataAndLoad(SessionDataManager.GameMode gameMode, SessionDataManager.SessionType sessionType)
    {
        if (!TryParseGender(Gender.text, out var parsedGender))
        {
            WriteMessage("Please enter a valid Gender value: Unspecified, Male, Female, NonBinary, Other.");
            return;
        }

        if (!TryParseDateMMDDYYYY(Date.text, out var normalizedDate))
        {
            WriteMessage("Please enter a valid date in MM/DD/YYYY format (example: 12/31/2025).");
            return;
        }

        AppManager.Instance.Session.BeginSession(
            Participant.text,
            parsedGender,
            normalizedDate,
            gameMode,
            sessionType
        );

        if (!Application.CanStreamedLevelBeLoaded(GameSceneName))
        {
            WriteMessage($"Scene '{GameSceneName}' cannot be loaded. Add it to Build Settings and check the name.");
            return;
        }

        SceneManager.LoadScene(GameSceneName);
    }

    private void GenerateSettingsUI()
    {
        // Clear so it doesn't break on reopen
        foreach (Transform child in settingsContentContainer) Destroy(child.gameObject);

        // Fetch and generate settings
        var allSettings = AppManager.Instance.Settings.SettingsList;
        foreach (var settingData in allSettings)
        {
            GameObject newRow = Instantiate(settingRowPrefab, settingsContentContainer);

            SettingRowUI rowScript = newRow.GetComponent<SettingRowUI>();
            rowScript.Setup(settingData);
        }
    }

    public void OpenPersistentDataPath()
    {
        string folderPath = Application.persistentDataPath;

        folderPath = folderPath.Replace("/", "\\");

        // Windows
        #if UNITY_STANDALONE_WIN
        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folderPath + "\"") { UseShellExecute = true });
        #endif

        // MAC OS AND LINUX ARE UNTESTED
        // MacOS
        #if UNITY_STANDALONE_OSX
        Process.Start(new ProcessStartInfo("open", "\"" + folderPath + "\"") { UseShellExecute = true });
        #endif

        // Linux
        #if UNITY_STANDALONE_LINUX
        Process.Start(new ProcessStartInfo("xdg-open", "\"" + folderPath + "\"") { UseShellExecute = true });
        #endif
    }

    public void CloseApplication()
    {
        #if UNITY_EDITOR
        // Stop play mode in the editor
        UnityEditor.EditorApplication.isPlaying = false;

        #else
        Application.Quit();
        #endif
    }

    public void WriteMessage(string error, Color? color = null, float errorTimeSeconds = 5f)
    {
        TMP_Text textField = (menuState == MenuState.Main) ? MainMenuErrorMessageTextField : SettingsMenuErrorMessageTextField;
        if (textField == null) return;

        textField.color = color ?? Color.red;
        textField.text = error ?? "";

        if (clearErrorCoroutine != null)
            StopCoroutine(clearErrorCoroutine);

        if (errorTimeSeconds > 0f)
            clearErrorCoroutine = StartCoroutine(ClearErrorAfterSeconds(textField, errorTimeSeconds));
    }

    private IEnumerator ClearErrorAfterSeconds(TMP_Text textField, float seconds)
    {
        yield return new WaitForSeconds(seconds);

        if (textField != null)
            textField.text = "";

        clearErrorCoroutine = null;
    }
}
