using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class DefaultTPPUI : MonoBehaviour
{
    [SerializeField] private ThreePerspectivePlayer characterController;
    [SerializeField] private Button RotLeftButton; // Q
    [SerializeField] private Button RotRightButton; // E
    [SerializeField] private Button ChangeFarViewButton; // R
    [SerializeField] private Button ChangePerspectiveButton; // B
    [SerializeField] private Button RecenterButton;
    [SerializeField] private Button UIFreezeButton;

    void Start()
    {
        RotLeftButton.onClick.AddListener(() => { RotateLeft(); });
        RotRightButton.onClick.AddListener(() => { RotateRight(); });
        ChangeFarViewButton.onClick.AddListener(() => { SwitchBirdsEyeViewModes(); });
        ChangePerspectiveButton.onClick.AddListener(() => { ChangePerspective(); });
        RecenterButton.onClick.AddListener(() => { characterController.CenterCamera(); });
        UIFreezeButton.onClick.AddListener(() => { characterController.ToggleFreezeState(!characterController.IsFrozen); });
    }

    private void RotateLeft()
    {
        if (characterController.CurrentMode == ThreePerspectivePlayer.Mode.FirstPerson) return;
        characterController.RotateCamera(45f, 0.2f);
    } 

    private void RotateRight()
    {
        if (characterController.CurrentMode == ThreePerspectivePlayer.Mode.FirstPerson) return;
        characterController.RotateCamera(-45f, 0.2f);
    }

    private void SwitchBirdsEyeViewModes()
    {
        if (characterController.CurrentMode == ThreePerspectivePlayer.Mode.FirstPerson) return;
        characterController.SwitchBirdsEyeViewModes();
    }

    private void ChangePerspective()
    {
        if (characterController.IsFrozen) return;
        ClearFocus(); 
        characterController.SwitchBetweenEditAndBirdsEyeViewModes();
    }

    public void ClearFocus()
    {
        EventSystem.current.SetSelectedGameObject(null);
    }
}
