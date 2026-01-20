using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GradientNavigationSceneManager : MonoBehaviour
{
    // ------------------------------------------------------------------------
    // STATE & VARIABLES
    // ------------------------------------------------------------------------

    // Current State
    private SessionDataManager.GameState state;

    // Trial Counters
    private int trialIndex;
    private int attemptsRemaining;
    private float timeRemaining;
    private bool trialComplete;

    // Current Trial Data
    private Vector2 startXZ;

    // Pause Logic
    private Vector2 pausedReturnXZ; // Where the player was when they paused

    // Training Flags
    private bool allowTrainingPause = false;

    // ------------------------------------------------------------------------
    // UNITY LIFECYCLE
    // ------------------------------------------------------------------------

    private void Start()
    {
        SetState(SessionDataManager.GameState.Idle);

        // Spawn the player at origin initially
        AppManager.Instance.Player.SpawnPlayer(Vector3.zero, Quaternion.identity);

        // Initialize the TrialManager (Loads CSV or preps Random Seed)
        AppManager.Instance.Trial.Init();

        // Begin the experiment flow
        StartCoroutine(RunAllTrials());
    }

    private void Update()
    {
        // 1. Always update passive systems
        AppManager.Instance.Logger.ManualUpdate();
        AppManager.Instance.Player.Minimap.ManualUpdate();

        // 2. Input: Pause / Unpause
        if (GetPauseToggleInput())
        {
            if (state == SessionDataManager.GameState.Trial)
                Pause();
            else if (state == SessionDataManager.GameState.Training && allowTrainingPause)
                Pause(false);
            else if (state == SessionDataManager.GameState.Paused && allowTrainingPause)
                Unpause(false);
            else if (state == SessionDataManager.GameState.Paused)
                Unpause();

            return; // Don't process other inputs this frame
        }

        // 3. Input: VR Recenter (Available generally if VR)
        if (AppManager.Instance.Session.IsVRMode && GetRecenteringInput())
        {
            AppManager.Instance.Player.TeleportVRToCoordinates(0, 0);
        }

        // 4. Game Logic (Only runs during Active Trial)
        if (state != SessionDataManager.GameState.Trial) return;

        AppManager.Instance.Player.UpdateStimulusUI();

        // Timer
        timeRemaining -= Time.deltaTime;
        if (timeRemaining <= 0f)
        {
            Debug.Log($"[Trial {trialIndex + 1}] Time expired.");
            AppManager.Instance.Player.SetUIMessage("", Color.white, -1);
            EndTrial();
            return;
        }

        // Submission
        if (GetSubmitInput())
        {
            HandleSubmission();
        }
    }

    // ------------------------------------------------------------------------
    // MAIN EXPERIMENT LOOP
    // ------------------------------------------------------------------------

    private IEnumerator RunAllTrials()
    {
        AppManager.Instance.Logger.BeginLogging();
        // --- PHASE 1: TRAINING ---
        if (AppManager.Instance.Settings.EnableTraining && AppManager.Instance.Session.IsVRMode)
        {
            yield return RunTrainingPhase();
        }

        // --- PHASE 2: EXPERIMENT TRIALS ---

        AppManager.Instance.Player.SetUIMessage("", Color.white, -1);

        int totalTrials = AppManager.Instance.Trial.GetTotalTrialCount();

        for (trialIndex = 0; trialIndex < totalTrials; trialIndex++)
        {
            // A. Get Data from TrialManager
            TrialSpec spec = AppManager.Instance.Trial.GetTrial(trialIndex);

            // B. Generate Map (Visuals + Heatmap math)
            // Note: Even if we loaded from CSV, we must Generate to set up the Stimulus intensity logic
            AppManager.Instance.Stimulus.GenerateMap(
                spec.MapTypeIndex,
                AppManager.Instance.Settings.MapWidth,
                AppManager.Instance.Settings.MapLength,
                spec.CenterXZ,
                goalOverride: spec.GoalOverride,
                multiPeakSpecs: spec.Peaks
            );

            // C. Setup Session Data
            startXZ = spec.SpawnXZ;
            Vector2 targetXZ = AppManager.Instance.Stimulus.GetTargetPosition(); // Truth source from Stimulus

            AppManager.Instance.Session.MapType = StimulusManager.MapTypes[spec.MapTypeIndex];
            AppManager.Instance.Player.Minimap.RefreshMinimap();
            AppManager.Instance.Session.TrialNumber = trialIndex + 1;
            AppManager.Instance.Session.SpawnPosition = startXZ;
            AppManager.Instance.Session.GoalPosition = targetXZ;

            // D. Reset Counters
            attemptsRemaining = AppManager.Instance.Settings.ParticipantMaxTestCount;
            timeRemaining = AppManager.Instance.Settings.TimeToSeek;
            trialComplete = false;

            // E. Move Player to Start
            if (!AppManager.Instance.Session.IsVRMode)
            {
                AppManager.Instance.Player.Teleport(startXZ.x, startXZ.y);
            }
            else
            {
                yield return WalkOrientTo(startXZ);
            }

            // F. Begin Trial
            SetState(SessionDataManager.GameState.Trial);
            AppManager.Instance.Logger.LogEvent($"TRIAL_START {trialIndex}");

            // G. Wait for Completion (EndTrial() sets trialComplete = true)
            yield return new WaitUntil(() => trialComplete);

            // H. Clean up
            SetState(SessionDataManager.GameState.Idle);
        }

        // --- PHASE 3: FINISH ---
        SetState(SessionDataManager.GameState.Idle);
        AppManager.Instance.Logger.EndLogging();
        SceneManager.LoadScene("Closing Scene");
    }

    // ------------------------------------------------------------------------
    // TRAINING PHASE
    // ------------------------------------------------------------------------

    private IEnumerator RunTrainingPhase()
    {
        AppManager.Instance.Player.EnableBlackscreen();
        SetState(SessionDataManager.GameState.Training);
        Debug.Log("Starting VR Training Phase...");

        AppManager.Instance.Player.ResizeTextWindow(new Vector3(0f, -0.5f, 0f), new Vector2(4, 3));
        // 1. Wait for Administrator
        AppManager.Instance.Player.SetUIMessage("[TRAINING]\nPlease wait as the study administrator provides an orientation.", Color.white, -1);
        yield return new WaitUntil(() => Input.GetKeyDown(KeyCode.Return));

        // 2. Brightness explanation
        string msg = $"The brightness of the screen will change as you move around the scene." +
                     $"\n(Press either trigger key to continue)";
        AppManager.Instance.Player.SetUIMessage(msg, Color.white, -1);
        yield return WaitForAnyTrigger();

        msg = $"When you think you are at the point of maximum brightness, press the trigger key on either of your controllers." +
              $"\n(Press either trigger key to continue)";
        AppManager.Instance.Player.SetUIMessage(msg, Color.white, -1);
        yield return WaitForAnyTrigger();

        msg = $"You will be given {AppManager.Instance.Settings.ParticipantMaxTestCount} attempt(s) to find the point of maximum brightness." +
              $"\n(Press either trigger key to continue)";
        AppManager.Instance.Player.SetUIMessage(msg, Color.white, -1);
        yield return WaitForAnyTrigger();

        // 3. Recenter
        AppManager.Instance.Player.SetUIMessage("Recenter the room now if necessary.\n(Press the select button on your right controller, or press either trigger key to skip this step)", Color.white, -1);
        yield return WaitForTriggerOrRightSelect();

        // 4. Pillar Explanation
        AppManager.Instance.Player.SetUIMessage("At the beginning of each trial, you will be asked to walk to a location, as specified by a red pillar.\n(Press either trigger key to continue)", Color.white, -1);
        yield return WaitForAnyTrigger();

        AppManager.Instance.Player.ResizeTextWindow(new Vector3(-2f, -0.5f, 0f), new Vector2(3, 3));

        // 5. Orientation Trial (3m away)
        AppManager.Instance.Player.SetUIMessage("", Color.white, -1);
        Vector2 target3m = CalculateSafe3mPoint();
        yield return WalkOrientTo(target3m, false);
        SetState(SessionDataManager.GameState.Training); // Restore state after orientation

        // 6. Safety Walls
        if (AppManager.Instance.Settings.EnableSafetyWalls)
        {
            AppManager.Instance.Player.ResizeTextWindow(new Vector3(0f, -0.5f, 0f), new Vector2(4, 3));
            AppManager.Instance.Player.SetUIMessage("Safety walls will warn you if you are too close to a wall. Walk to the pillar at the corner of the room.\n(Press either trigger key to continue)", Color.white, -1);
            yield return WaitForAnyTrigger();
            AppManager.Instance.Player.ResizeTextWindow(new Vector3(-2f, -0.5f, 0f), new Vector2(3, 3));
            Vector2 cornerPos = GetClosestCornerInset(1f);
            yield return WalkOrientTo(cornerPos, false);
            SetState(SessionDataManager.GameState.Training);
            AppManager.Instance.Player.ResizeTextWindow(new Vector3(0f, -0.5f, 0f), new Vector2(4, 3));
            AppManager.Instance.Player.SetUIMessage("Check to ensure the safety indicators appear.\n(Press either trigger key to continue)", Color.white, -1);
            yield return WaitForAnyTrigger();
            AppManager.Instance.Player.ResizeTextWindow(new Vector3(-2f, -0.5f, 0f), new Vector2(3, 3));
        }

        // 7. Pause Training
        if (AppManager.Instance.Settings.EnablePause)
        {
            AppManager.Instance.Player.ResizeTextWindow(new Vector3(0f, -0.5f, 0f), new Vector2(4, 3));
            AppManager.Instance.Player.SetUIMessage("At any time you can pause the trial if you feel physical discomfort. Press the select button on your left hand to pause and unpause.", Color.white, -1);

            allowTrainingPause = true;

            // Wait for user to Pause
            yield return new WaitUntil(() => state == SessionDataManager.GameState.Paused);
            AppManager.Instance.Player.ResizeTextWindow(new Vector3(-2f, -0.5f, 0f), new Vector2(3, 3));

            // Wait for user to Unpause
            yield return new WaitUntil(() => state != SessionDataManager.GameState.Paused);

            allowTrainingPause = false;
            SetState(SessionDataManager.GameState.Training);
        }

        // 8. Ready
        AppManager.Instance.Player.ResizeTextWindow(new Vector3(0f, -0.5f, 0f), new Vector2(4, 3));
        AppManager.Instance.Player.SetUIMessage("You are now ready to begin the study. You may proceed when ready.\n(Press either trigger key to continue)", Color.white, -1);
        yield return WaitForAnyTrigger();
        AppManager.Instance.Player.ResizeTextWindow(new Vector3(-2f, -0.5f, 0f), new Vector2(3, 3));

        AppManager.Instance.Player.SetUIMessage("", Color.white, -1);
        AppManager.Instance.Player.DisableBlackscreen();
    }

    // ------------------------------------------------------------------------
    // STATE & SUBMISSION LOGIC
    // ------------------------------------------------------------------------

    private void SetState(SessionDataManager.GameState gameState)
    {
        state = gameState;
        AppManager.Instance.Session.State = gameState;
    }

    private void HandleSubmission()
    {
        AppManager.Instance.Player.SetUIMessage("", Color.white, -1);

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

        if (attemptsRemaining != -1 && attemptsRemaining <= 0)
        {
            Debug.Log($"[Trial {trialIndex + 1}] Failed (no attempts remaining).");
            AppManager.Instance.Logger.LogEvent($"TRIAL_FAIL {trialIndex}");
            EndTrial();
            return;
        }

        AppManager.Instance.Player.SetUIMessage("Try again!", Color.magenta, 4);
    }

    private void EndTrial()
    {
        if (state != SessionDataManager.GameState.Trial) return;

        SetState(SessionDataManager.GameState.Idle);
        trialComplete = true;
    }

    // ------------------------------------------------------------------------
    // PAUSE SYSTEM
    // ------------------------------------------------------------------------

    private void Pause(bool adjustBlackscreen = true)
    {
        // Double check validity
        bool validState = state == SessionDataManager.GameState.Trial ||
                          (state == SessionDataManager.GameState.Training && allowTrainingPause);

        if (!validState) return;

        // Save position for return
        Vector3 camPos = AppManager.Instance.Player.CameraPosition().position;
        pausedReturnXZ = new Vector2(camPos.x, camPos.z);

        SetState(SessionDataManager.GameState.Paused);

        // Desktop: toggle movement
        if (!AppManager.Instance.Session.IsVRMode)
            AppManager.Instance.Player.ToggleMovement();

        // Visual feedback
        AppManager.Instance.Player.ResizeTextWindow(new Vector3(-2f, -0.5f, 0f), new Vector2(3, 3));
        AppManager.Instance.Player.SetUIMessage("Paused", Color.white, -1);
        AppManager.Instance.Logger.LogEvent("PAUSED");
        if (adjustBlackscreen) AppManager.Instance.Player.EnableBlackscreen();
    }

    private void Unpause(bool adjustBlackscreen = true)
    {
        if (state != SessionDataManager.GameState.Paused) return;

        AppManager.Instance.Player.SetUIMessage("", Color.white, -1);
        AppManager.Instance.Logger.LogEvent("UNPAUSED");
        if (adjustBlackscreen) AppManager.Instance.Player.DisableBlackscreen();

        // Desktop Handling
        if (!AppManager.Instance.Session.IsVRMode)
        {
            AppManager.Instance.Player.ToggleMovement();
            RestoreStateAfterUnpause();
            return;
        }

        // VR Handling
        if (AppManager.Instance.Settings.ReorientAfterPause)
        {
            StartCoroutine(UnpauseVRRoutine());
        }
        else
        {
            RestoreStateAfterUnpause();
        }
    }

    private IEnumerator UnpauseVRRoutine()
    {
        // Force them to walk back to where they paused to realign physical space
        yield return WalkOrientTo(pausedReturnXZ);
        RestoreStateAfterUnpause();
    }

    private void RestoreStateAfterUnpause()
    {
        if (allowTrainingPause) SetState(SessionDataManager.GameState.Training);
        else SetState(SessionDataManager.GameState.Trial);
    }

    // ------------------------------------------------------------------------
    // ORIENTATION (VR ONLY)
    // ------------------------------------------------------------------------

    private IEnumerator WalkOrientTo(Vector2 xz, bool adjustBlackscreen = true)
    {
        if (!AppManager.Instance.Session.IsVRMode) yield break;

        SetState(SessionDataManager.GameState.Orient);
        AppManager.Instance.Logger.LogEvent($"ORIENTATION_START {trialIndex}");

        if (adjustBlackscreen) AppManager.Instance.Player.EnableBlackscreen();
        yield return AppManager.Instance.Orientation.WalkToLocation(xz.x, xz.y);
        if (adjustBlackscreen) AppManager.Instance.Player.DisableBlackscreen();

        AppManager.Instance.Logger.LogEvent($"ORIENTATION_END {trialIndex}");
    }

    // ------------------------------------------------------------------------
    // INPUT HELPERS
    // ------------------------------------------------------------------------

    private bool GetSubmitInput()
    {
        if (state != SessionDataManager.GameState.Trial) return false;

        // Desktop
        if (!AppManager.Instance.Session.IsVRMode && Input.GetKeyDown(KeyCode.Return))
            return true;

        // VR
        bool left = AppManager.Instance.LeftActivate.action != null && AppManager.Instance.LeftActivate.action.WasPressedThisFrame();
        bool right = AppManager.Instance.RightActivate.action != null && AppManager.Instance.RightActivate.action.WasPressedThisFrame();
        return left || right;
    }

    private bool GetPauseToggleInput()
    {
        if (!AppManager.Instance.Settings.EnablePause) return false;

        // Check if current state allows pausing
        bool inTrial = state == SessionDataManager.GameState.Trial;
        bool inPause = state == SessionDataManager.GameState.Paused;
        bool inTraining = state == SessionDataManager.GameState.Training && allowTrainingPause;

        if (!inTrial && !inPause && !inTraining) return false;

        // Desktop
        if (!AppManager.Instance.Session.IsVRMode) return Input.GetKeyDown(KeyCode.Space);

        // VR (Left Select)
        if (AppManager.Instance.LeftSelect.action != null && AppManager.Instance.LeftSelect.action.WasPressedThisFrame())
            return true;

        return false;
    }

    private bool GetRecenteringInput()
    {
        // R key or Right Select
        return Input.GetKeyDown(KeyCode.R) ||
               (AppManager.Instance.Session.IsVRMode &&
                AppManager.Instance.RightSelect.action != null &&
                AppManager.Instance.RightSelect.action.WasPressedThisFrame());
    }

    // ------------------------------------------------------------------------
    // TRAINING HELPERS
    // ------------------------------------------------------------------------

    private IEnumerator WaitForAnyTrigger()
    {
        yield return new WaitForSeconds(0.5f); // Debounce
        yield return new WaitUntil(() =>
            (AppManager.Instance.LeftActivate.action != null && AppManager.Instance.LeftActivate.action.WasPressedThisFrame()) ||
            (AppManager.Instance.RightActivate.action != null && AppManager.Instance.RightActivate.action.WasPressedThisFrame())
        );
    }

    private IEnumerator WaitForTriggerOrRightSelect()
    {
        yield return new WaitForSeconds(0.5f); // Debounce
        yield return new WaitUntil(() =>
            (AppManager.Instance.LeftActivate.action != null && AppManager.Instance.LeftActivate.action.WasPressedThisFrame()) ||
            (AppManager.Instance.RightActivate.action != null && AppManager.Instance.RightActivate.action.WasPressedThisFrame()) ||
            (AppManager.Instance.RightSelect.action != null && AppManager.Instance.RightSelect.action.WasPressedThisFrame())
        );
    }

    private Vector2 CalculateSafe3mPoint()
    {
        Transform player = AppManager.Instance.Player.transform;
        Vector3 origin = player.position;
        float w = AppManager.Instance.Settings.MapWidth / 2f;
        float l = AppManager.Instance.Settings.MapLength / 2f;

        Vector3[] directions = { player.forward, -player.forward, player.right, -player.right };

        foreach (var dir in directions)
        {
            Vector3 target = origin + (dir * 3f);
            if (target.x >= -w && target.x <= w && target.z >= -l && target.z <= l)
            {
                return new Vector2(target.x, target.z);
            }
        }
        return Vector2.zero;
    }

    private Vector2 GetClosestCornerInset(float inset)
    {
        Transform player = AppManager.Instance.Player.transform;
        float w = AppManager.Instance.Settings.MapWidth / 2f;
        float l = AppManager.Instance.Settings.MapLength / 2f;

        float xSign = (player.position.x >= 0) ? 1 : -1;
        float zSign = (player.position.z >= 0) ? 1 : -1;

        float targetX = (w * xSign) - (inset * xSign);
        float targetZ = (l * zSign) - (inset * zSign);

        return new Vector2(targetX, targetZ);
    }
}