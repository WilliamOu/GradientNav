using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TPPUITemplate : MonoBehaviour
{
    public ThreePerspectivePlayer playerController;
    public GameObject settingsMenuCanvas;
    public GameObject recordingHUD;

    void OnEnable()
    {
        // Subscribe to events
        playerController.OnFreezeStateChanged += HandleFreeze;
        playerController.OnViewModeChanged += HandleModeChange;
    }

    void OnDisable()
    {
        // Always unsubscribe to prevent memory leaks
        playerController.OnFreezeStateChanged -= HandleFreeze;
        playerController.OnViewModeChanged -= HandleModeChange;
    }

    // This runs automatically when you hit 'Alt' in the player script
    private void HandleFreeze(bool isFrozen)
    {
        settingsMenuCanvas.SetActive(isFrozen);

        // Example: Maybe you want to hide the recording HUD when the menu is up
        recordingHUD.SetActive(!isFrozen);
    }

    // This runs automatically when you switch views (Iso/Top/FP)
    private void HandleModeChange(ThreePerspectivePlayer.Mode newMode)
    {
        Debug.Log($"Study Logger: User switched to {newMode}");
        // You could trigger recording logic here, or change UI prompts
    }
}
