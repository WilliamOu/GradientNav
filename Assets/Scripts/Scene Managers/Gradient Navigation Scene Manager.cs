using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// TODO: The following gamemode is a TEST gamemode. Ask lab for further implementation details
public class GradientNavigationSceneManager : MonoBehaviour
{
    [Header("Trial Config")]
    [SerializeField] private float minStartDistance = 3.0f; // Don't spawn player on top of goal
    public InputActionProperty leftTriggerInput;
    public InputActionProperty rightTriggerInput;
    public InputActionProperty leftGripInput;
    public InputActionProperty rightGripInput;

    // State
    private int attemptsRemaining;
    private float timeRemaining; // New Timer
    private bool isTrialActive = false;

    private void Start()
    {
        // Setup Trial Data
        attemptsRemaining = AppManager.Instance.Settings.ParticipantMaxTestCount;

        // Load TimeToSeek from settings
        timeRemaining = AppManager.Instance.Settings.TimeToSeek;

        // Randomize Locations
        SetupPositions();

        // Start Logging
        AppManager.Instance.Logger.BeginLogging();
        isTrialActive = true;
    }

    private void Update()
    {
        if (!isTrialActive) return;

        AppManager.Instance.Logger.ManualUpdate();
        AppManager.Instance.Player.UpdateStimulusUI();

        timeRemaining -= Time.deltaTime;
        if (timeRemaining <= 0)
        {
            Debug.Log("[Trial] Time Expired.");
            HandleSubmission();
            return;
        }

        // Check Input
        if (GetSubmitInput())
        {
            HandleSubmission();
        }
    }

    private void SetupPositions()
    {
        // Get Map Bounds from Settings
        float width = AppManager.Instance.Settings.MapWidth;
        float length = AppManager.Instance.Settings.MapLength;

        // Define usable radius (slightly smaller than map to avoid clipping walls)
        float spawnRadiusX = (width / 2f) * 0.9f;
        float spawnRadiusZ = (length / 2f) * 0.9f;

        Vector3 startPos = Vector3.zero;
        Vector2 targetPos = Vector2.zero;

        // Simple Randomization Loop
        // Keep picking positions until they are far enough apart
        int safetyBreak = 0;
        do
        {
            // Random Player Start
            float startX = Random.Range(-spawnRadiusX, spawnRadiusX);
            float startZ = Random.Range(-spawnRadiusZ, spawnRadiusZ);
            startPos = new Vector3(startX, 0, startZ);

            // Random Target (Gaussian Center)
            float targX = Random.Range(-spawnRadiusX, spawnRadiusX);
            float targZ = Random.Range(-spawnRadiusZ, spawnRadiusZ);
            targetPos = new Vector2(targX, targZ);

            safetyBreak++;
        }
        while (Vector2.Distance(new Vector2(startPos.x, startPos.z), targetPos) < minStartDistance && safetyBreak < 100);

        // Apply to Systems
        AppManager.Instance.Player.SetStimulusTarget(targetPos);

        // Spawn Player (Random Rotation)
        Quaternion randomRot = Quaternion.Euler(0, Random.Range(0, 360), 0);
        AppManager.Instance.Player.SpawnPlayer(startPos, randomRot);
    }

    private bool GetSubmitInput()
    {
        // Desktop
        if (/*!AppManager.Instance.Session.IsVRMode && */Input.GetKeyDown(KeyCode.Return)) return true;

        float leftTriggerValue = leftTriggerInput.action.ReadValue<float>();
        float rightTriggerValue = rightTriggerInput.action.ReadValue<float>();
        float leftGripValue = leftGripInput.action.ReadValue<float>();
        float rightGripValue = rightGripInput.action.ReadValue<float>();

        // VR Check
        if (AppManager.Instance.Session.IsVRMode && (leftTriggerValue > 0.5f || rightTriggerValue > 0.5f))
        {
            return true;
        }

        return false;
    }

    private void HandleSubmission()
    {
        float currentIntensity = AppManager.Instance.Player.StimulusIntensity;
        bool isSuccess = currentIntensity >= AppManager.Instance.Settings.SuccessThreshold;

        if (isSuccess)
        {
            EndTrial(true);
        }
        else
        {
            attemptsRemaining--;
            // Check if we ran out of attempts (handle -1 infinite case)
            if (attemptsRemaining != -1 && attemptsRemaining <= 0)
            {
                Debug.Log("[Trial] No attempts remaining.");
                EndTrial(false);
            }
        }
    }

    private void EndTrial(bool success)
    {
        if (success) Debug.Log("Trial succeeeded");
        else Debug.Log("Trial failed");

        isTrialActive = false;
        AppManager.Instance.Logger.EndLogging();
        SceneManager.LoadScene("Closing Scene");
    }
}