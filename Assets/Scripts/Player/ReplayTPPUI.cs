using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Unity.VisualScripting;

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

    [Header("Timeline")]
    [SerializeField] private Slider timelineSlider;
    [SerializeField] private TMP_Text currentTimeText;
    [SerializeField] private TMP_Text currentPlaybackSpeedText;
    [SerializeField] private TMP_Text autoAlignText;

    private bool isDraggingSlider = false;

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
            pauseBtn.onClick.AddListener(AppManager.Instance.Replay.TogglePlayPause);

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

        autoAlignText.text = AppManager.Instance.Replay.ContinuousAutoAlign ? "Auto Align: On" : "Auto Align: Off";
        currentPlaybackSpeedText.text = "Speed: " + Mathf.Round(AppManager.Instance.Replay.PlaybackSpeed * 100f) / 100f + "x";
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

    private void OnReturnToTitle()
    {
        AppManager.Instance.MapVisualizer.ToggleMap(false);
        SceneManager.LoadScene("Title Scene");
    }

    void Update()
    {
        if (AppManager.Instance.Replay == null) return;

        // Sync Slider & Text (Only if user is NOT dragging)
        if (!isDraggingSlider && timelineSlider != null)
        {
            timelineSlider.minValue = 0f;
            timelineSlider.maxValue = AppManager.Instance.Replay.MaxTime;
            timelineSlider.value = AppManager.Instance.Replay.CurrentTime;
        }

        // Sync Text Labels
        currentTimeText.text = FormatTime(AppManager.Instance.Replay.CurrentTime) + " / " + FormatTime(AppManager.Instance.Replay.MaxTime);

        // Optional: Update Play/Pause button text based on state
        if (pauseBtn != null)
        {
            Text btnText = pauseBtn.GetComponentInChildren<Text>();
            if (btnText) btnText.text = AppManager.Instance.Replay.IsPlaying ? "Pause" : "Play";
        }
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