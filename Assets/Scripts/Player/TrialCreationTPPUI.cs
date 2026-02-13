using UnityEngine;

public class TrialCreationTPPUI : MonoBehaviour
{
    [SerializeField] private ThreePerspectivePlayer playerController;

    void OnEnable()
    {
        // Subscribe to events
        playerController.OnFreezeStateChanged += HandleFreeze;
        playerController.OnViewModeChanged += HandleModeChange;
    }

    void OnDisable()
    {
        // Unsubscribe to prevent memory leaks
        playerController.OnFreezeStateChanged -= HandleFreeze;
        playerController.OnViewModeChanged -= HandleModeChange;
    }

    private void HandleFreeze(bool isFrozen)
    {
        
    }

    // This runs automatically when you switch views (Iso/Top/FP)
    private void HandleModeChange(ThreePerspectivePlayer.Mode newMode)
    {
        
    }
}