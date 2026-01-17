using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class GradientNavigationSceneManager : MonoBehaviour
{
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
        // Pause/unpause toggle
        if (GetPauseToggleInput())
        {
            if (state == SessionDataManager.GameState.Trial) Pause();
            else if (state == SessionDataManager.GameState.Paused) Unpause();
            return;
        }

        AppManager.Instance.Logger.ManualUpdate();

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
        AppManager.Instance.Player.SetUIMessage("", Color.white, -1);
        AppManager.Instance.Logger.BeginLogging();
        int totalTrials = AppManager.Instance.Settings.TrialCount;

        for (trialIndex = 0; trialIndex < totalTrials; trialIndex++)
        {
            // Pick new randomized positions every loop
            SetupPositions(out startXZ, out targetXZ);

            AppManager.Instance.Session.TrialNumber = trialIndex + 1;
            AppManager.Instance.Session.SpawnPosition = startXZ;
            AppManager.Instance.Session.GoalPosition = targetXZ;

            // Reset per-trial counters
            attemptsRemaining = AppManager.Instance.Settings.ParticipantMaxTestCount;
            timeRemaining = AppManager.Instance.Settings.TimeToSeek;
            trialComplete = false;

            /*if (trialIndex == 0)
                AppManager.Instance.Logger.BeginLogging();
            else
                AppManager.Instance.Logger.ResumeLogging();*/

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

            /*if (trialIndex == totalTrials - 1)
                AppManager.Instance.Logger.EndLogging();
            else
                AppManager.Instance.Logger.PauseLogging();*/

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

        // Desktop: toggle movement as requested
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

    private void SetupPositions(out Vector2 outStartXZ, out Vector2 outTargetXZ)
    {
        float width = AppManager.Instance.Settings.MapWidth;
        float length = AppManager.Instance.Settings.MapLength;

        float spawnRadiusX = (width / 2f) * 0.9f;
        float spawnRadiusZ = (length / 2f) * 0.9f;

        Vector2 s = Vector2.zero;
        Vector2 t = Vector2.zero;

        int safetyBreak = 0;
        do
        {
            s = new Vector2(
                Random.Range(-spawnRadiusX, spawnRadiusX),
                Random.Range(-spawnRadiusZ, spawnRadiusZ)
            );

            t = new Vector2(
                Random.Range(-spawnRadiusX, spawnRadiusX),
                Random.Range(-spawnRadiusZ, spawnRadiusZ)
            );

            safetyBreak++;
        }
        while (Vector2.Distance(s, t) < minStartDistance && safetyBreak < 100);

        outStartXZ = s;
        outTargetXZ = t;
    }

    // ----------- Inputs -----------
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
}
