using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.IO;

using static ReplayManager;

public class ReplayTPPUI : MonoBehaviour
{
    [Header("References")]
    public ThreePerspectivePlayer playerController;

    [Header("UI Panels")]
    [SerializeField] private GameObject panel;

    [Header("Navigation")]
    [SerializeField] private Button returnToTitleSceneBtn;

    [Header("Playback Controls")]
    [SerializeField] private Button frameFwdBtn;
    [SerializeField] private Button frameBackBtn;
    [SerializeField] private Button fwdBtn;
    [SerializeField] private Button backBtn;
    [SerializeField] private Button increasePlaybackSpeedBtn;
    [SerializeField] private Button decreasePlaybackSpeedBtn;
    [SerializeField] private Button pauseBtn;
    [SerializeField] private Button toggleAutoAlign;
    [SerializeField] private Button autoAlign;
    [SerializeField] private TMP_Text btnText;

    [Header("Timeline")]
    [SerializeField] private Slider timelineSlider;
    [SerializeField] private TMP_Text currentTimeText;
    [SerializeField] private TMP_Text currentPlaybackSpeedText;
    [SerializeField] private TMP_Text autoAlignText;

    private bool isDraggingSlider = false;

    [Header("Visualization")]
    [SerializeField] private GameObject OrientPillar;
    [SerializeField] private GameObject NearsightIndicator;
    [SerializeField] private TMP_Text StateText;
    [SerializeField] private TMP_Text TrialText;
    [SerializeField] private TMP_Text StimulusText;
    private bool lastOrientState = false;
    private bool lastBlackoutNearsightIndicatorState = false;
    private float stimulusIntensity = 0.0f;
    private int lastTrial = -1;
    private byte lastState = 9;
    private string currentStateString = "UNKNOWN";
    private List<TrialSpec> trialSpecs = new List<TrialSpec>();
    bool trialsValid = true;

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
        // if (panel != null) panel.SetActive(true);

        try
        {
            AppManager.Instance.Settings.LoadFromDisk(Path.Combine(AppManager.Instance.Replay.GetFolderPath(), "settings_snapshot.json")); // Load snapshot settings
        }
        catch
        {
            // Do nothing though the Settings may be malformed
        }

        try
        {
            trialSpecs = TrialManager.LoadTrialsFromCsv(Path.Combine(AppManager.Instance.Replay.GetFolderPath(), "trials_snapshot.csv"));
        }
        catch
        {
            trialsValid = false;
        }

        if (returnToTitleSceneBtn)
            returnToTitleSceneBtn.onClick.AddListener(OnReturnToTitle);

        if (AppManager.Instance.Replay != null)
        {
            // Frame Stepping
            frameFwdBtn.onClick.AddListener(() => AppManager.Instance.Replay.StepFrameForward());
            frameBackBtn.onClick.AddListener(() => AppManager.Instance.Replay.StepFrameBack());

            // Big Skips (e.g., +/- 5 seconds)
            fwdBtn.onClick.AddListener(() => AppManager.Instance.Replay.SetTime(AppManager.Instance.Replay.CurrentTime + 5.0f));
            backBtn.onClick.AddListener(() => AppManager.Instance.Replay.SetTime(AppManager.Instance.Replay.CurrentTime - 5.0f));

            // Speed
            increasePlaybackSpeedBtn.onClick.AddListener(IncreasePlaybackSpeed);
            decreasePlaybackSpeedBtn.onClick.AddListener(DecreasePlaybackSpeed);

            // Play/Pause
            pauseBtn.onClick.AddListener(TogglePlayPause);

            // Settings
            toggleAutoAlign.onClick.AddListener(ToggleAutoAlign);
            autoAlign.onClick.AddListener(TriggerAutoAlign);

            // Slider Interaction
            if (timelineSlider != null)
            {
                // Add EventTrigger to detect dragging vs code updates
                EventTrigger trigger = timelineSlider.gameObject.AddComponent<EventTrigger>();

                var pointerDown = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
                pointerDown.callback.AddListener((e) => isDraggingSlider = true);
                trigger.triggers.Add(pointerDown);

                var pointerUp = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
                pointerUp.callback.AddListener((e) => {
                    isDraggingSlider = false;
                    AppManager.Instance.Replay.SetTime(timelineSlider.value);
                });
                trigger.triggers.Add(pointerUp);

                // Also listen for clicks on the bar
                timelineSlider.onValueChanged.AddListener(OnSliderValueChanged);
            }
        }

        // Initial State Check
        if (playerController != null)
        {
            HandleFreeze(playerController.IsFrozen);
        }

        // UI State initialization
        btnText.text = AppManager.Instance.Replay.IsPlaying ? "| |" : "[•]";
        autoAlignText.text = AppManager.Instance.Replay.ContinuousAutoAlign ? "Auto Align: On" : "Auto Align: Off";
        currentPlaybackSpeedText.text = "Speed: " + Mathf.Round(AppManager.Instance.Replay.PlaybackSpeed * 100f) / 100f + "x";

        // Replay Visuals (see LateUpdate)
        OrientPillar.gameObject.SetActive(false);
        NearsightIndicator.gameObject.SetActive(false);
        AppManager.Instance.Utilities.Minimap.ToggleMinimap(true);
    }

    private void IncreasePlaybackSpeed()
    {
        AppManager.Instance.Replay.IncreasePlaybackSpeed();
        currentPlaybackSpeedText.text = "Speed: " + Mathf.Round(AppManager.Instance.Replay.PlaybackSpeed * 100f) / 100f + "x";
    }

    private void DecreasePlaybackSpeed()
    {
        AppManager.Instance.Replay.DecreasePlaybackSpeed();
        currentPlaybackSpeedText.text = "Speed: " + Mathf.Round(AppManager.Instance.Replay.PlaybackSpeed * 100f) / 100f + "x";
    }

    private void ToggleAutoAlign()
    {
        AppManager.Instance.Replay.ToggleAutoAlign();
        autoAlignText.text = AppManager.Instance.Replay.ContinuousAutoAlign ? "Auto Align: On" : "Auto Align: Off";
    }

    private void TriggerAutoAlign()
    {
        AppManager.Instance.Replay.CalculateAutoAlignYaw();
        AppManager.Instance.Replay.ApplyShadowRotation();
    }

    private void TogglePlayPause()
    {
        AppManager.Instance.Replay.TogglePlayPause();
        btnText.text = AppManager.Instance.Replay.IsPlaying ? "| |" : "[•]";
    }

    private void OnReturnToTitle()
    {
        AppManager.Instance.Utilities.MapVisualizer.ToggleMap(false);
        AppManager.Instance.Utilities.Minimap.ToggleMinimap(false);
        AppManager.Instance.Settings.LoadFromDisk(); // Restore standard settings
        SceneManager.LoadScene("Title Scene");
    }

    void Update()
    {
        if (AppManager.Instance.Replay == null) return;

        // Pause/Play
        if (Input.GetKeyDown(KeyCode.Space))
        {
            TogglePlayPause();
        }

        // Forward and Back (Big Skips)
        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            AppManager.Instance.Replay.SetTime(AppManager.Instance.Replay.CurrentTime + 5.0f);
        }
        else if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            AppManager.Instance.Replay.SetTime(AppManager.Instance.Replay.CurrentTime - 5.0f);
        }

        // Frame Forward and Back
        if (Input.GetKeyDown(KeyCode.Period))
        {
            AppManager.Instance.Replay.StepFrameForward();
        }
        else if (Input.GetKeyDown(KeyCode.Comma))
        {
            AppManager.Instance.Replay.StepFrameBack();
        }

        // Increase/Decrease Playback Speed
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.Plus) || Input.GetKeyDown(KeyCode.KeypadPlus))
        {
            IncreasePlaybackSpeed();
        }
        else if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
        {
            DecreasePlaybackSpeed();
        }

        // Sync Slider & Text (Only if user is NOT dragging)
        if (!isDraggingSlider && timelineSlider != null)
        {
            timelineSlider.minValue = 0f;
            timelineSlider.maxValue = AppManager.Instance.Replay.MaxTime;
            timelineSlider.value = AppManager.Instance.Replay.CurrentTime;
        }

        // Sync Text Labels
        currentTimeText.text = FormatTime(AppManager.Instance.Replay.CurrentTime) + " / " + FormatTime(AppManager.Instance.Replay.MaxTime);
    }

    void LateUpdate()
    {
        var (frameA, frameB, t) = AppManager.Instance.Replay.GetCurrentXriFrames();
        if (frameA == null || frameB == null) return;

        Transform interpolatedHeadTransform = AppManager.Instance.Replay.GetXriHeadTransform();

        // Choose the closest frame mathematically to avoid binary states interpolating
        XriFrame closestFrame = (t < 0.5f) ? frameA : frameB;

        // So we don't call a chain of ifs every update
        if (closestFrame.State != lastState)
        {
            lastState = closestFrame.State;
            currentStateString = ReplaySelectionManager.DecodeState(lastState);
        }

        bool currOrientState = (closestFrame.State == 2);
        if (lastOrientState != currOrientState)
        {
            OrientPillar.gameObject.SetActive(currOrientState);
            OrientPillar.gameObject.transform.position = closestFrame.SpawnPos;
            lastOrientState = currOrientState;
        }

        NearsightIndicator.gameObject.transform.position = interpolatedHeadTransform.position;
        bool currNearsightState = (closestFrame.State != 4);
        if (lastBlackoutNearsightIndicatorState != currNearsightState)
        {
            NearsightIndicator.gameObject.SetActive(currNearsightState);
            lastBlackoutNearsightIndicatorState = currNearsightState;
        }

        int currentTrial = closestFrame.TrialNum;
        if (currentTrial != lastTrial && trialsValid)
        {
            TrialSpec spec = trialSpecs[currentTrial];

            AppManager.Instance.Stimulus.GenerateMap(
                spec.MapTypeIndex,
                AppManager.Instance.Settings.MapWidth,
                AppManager.Instance.Settings.MapLength,
                spec.CenterXZ,
                goalOverride: spec.GoalOverride,
                multiPeakSpecs: spec.Peaks,
                sigmaOverride: spec.SigmaOverride
            );

            // Setup Session Data
            AppManager.Instance.Utilities.Minimap.RefreshMinimap();
            lastTrial = currentTrial;
        }
        AppManager.Instance.Utilities.Minimap.ManualUpdate(interpolatedHeadTransform);

        if (closestFrame.State == 4)
        {
            stimulusIntensity = AppManager.Instance.Stimulus.GetIntensity(interpolatedHeadTransform.position);
        }
        else
        {
            stimulusIntensity = -1f;
        }

        // UI updates
        StateText.text = "State: " + currentStateString;
        TrialText.text = "Trial: " + currentTrial;
        StimulusText.text = (closestFrame.State == 4) ? "Stimulus: " + stimulusIntensity.ToString("F2") : "Stimulus: -";
    }

    void OnDestroy()
    {
        if (playerController != null) playerController.OnFreezeStateChanged -= HandleFreeze;
    }

    // --- Callbacks ---

    private void OnSliderValueChanged(float value)
    {
        // Only update replay if we are dragging, otherwise Update loop handles it
        if (isDraggingSlider && AppManager.Instance.Replay != null)
        {
            AppManager.Instance.Replay.SetPlayState(false); // Pause while scrubbing
            AppManager.Instance.Replay.SetTime(value);
        }
    }

    private void HandleFreeze(bool isFrozen)
    {
        if (panel == null) return;

        // panel.SetActive(isFrozen);

        if (!isFrozen)
        {
            // Clear selection so pressing 'Space' doesn't re-trigger the last button
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }
    }

    // --- Helpers ---

    private string FormatTime(float timeInSeconds)
    {
        int minutes = Mathf.FloorToInt(timeInSeconds / 60F);
        int seconds = Mathf.FloorToInt(timeInSeconds % 60F);
        int milliseconds = Mathf.FloorToInt((timeInSeconds * 100F) % 100F);
        return string.Format("{0:00}:{1:00}.{2:00}", minutes, seconds, milliseconds);
    }
}