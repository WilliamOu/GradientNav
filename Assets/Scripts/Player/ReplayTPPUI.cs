using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ReplayTPPUI : MonoBehaviour
{
    public ThreePerspectivePlayer playerController;
    [SerializeField] private GameObject panel;
    [SerializeField] private Button backBtn;
    [SerializeField] private Button frameFwdBtn;
    [SerializeField] private Button frameBackBtn;
    [SerializeField] private Button fastFwdBtn;
    [SerializeField] private Button fastBackBtn;
    [SerializeField] private Button pauseBtn;

    void OnEnable()
    {
        if (playerController != null)
        {
            playerController.OnFreezeStateChanged += HandleFreeze;
        }
    }

    void OnDisable()
    {
        if (playerController != null)
        {
            playerController.OnFreezeStateChanged -= HandleFreeze;
        }
    }

    void Start()
    {
        if (panel != null) panel.SetActive(true);

        // TODO: Add listeners

        if (playerController != null)
        {
            // Subscribe
            playerController.OnFreezeStateChanged += HandleFreeze;

            HandleFreeze(playerController.IsFrozen);
        }
        // if (panel != null) panel.SetActive(false);
        AppManager.Instance.MapVisualizer.ToggleMap(true);
    }

    void OnDestroy()
    {
        if (playerController != null) playerController.OnFreezeStateChanged -= HandleFreeze;
    }

    // UI Visibility Logic
    private void HandleFreeze(bool isFrozen)
    {
        if (panel == null) return;

        // 1. Hard Toggle: If Frozen, Panel ON. If Unfrozen, Panel OFF.
        panel.SetActive(isFrozen);

        // 2. Focus Safety: If we just Unfroze, force Unity to "forget" the last text box.
        // This ensures pressing 'W' doesn't secretly type into the now-invisible input field.
        if (!isFrozen)
        {
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }
    }
}
