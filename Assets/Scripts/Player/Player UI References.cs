using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit;

public class PlayerUIReferences : MonoBehaviour
{
    public Camera PlayerCamera;
    public TextMeshProUGUI UIText;
    public Image GradientImage;
    public Canvas MainCanvas;
    public Canvas ColorCanvas;
    public Image Blackscreen;
    public CharacterController CController;
    public ActionBasedContinuousMoveProvider ContinuousMoveProvider;
}