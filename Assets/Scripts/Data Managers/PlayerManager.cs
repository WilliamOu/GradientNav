using UnityEngine;
using System.Collections;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using TMPro;

public class PlayerManager : MonoBehaviour
{
    public bool PlayerSpawned { get; private set; }
    public float StimulusIntensity { get; private set; } = -1f;
    public bool CanMove { get; private set; } = true;
    public bool CanLook { get; private set; } = true;

    private GameObject vrPlayerPrefab;
    private GameObject desktopPlayerPrefab;
    private GameObject activePlayerInstance;
    private Coroutine clearUITextCoroutine;
    private PlayerUIReferences activeUI;
    private TeleportationProvider teleportationProvider;
    private Transform xrOrigin;
    private InputDevice leftHandDevice;
    private InputDevice rightHandDevice;

    public void Init(GameObject vrPlayerPrefab, GameObject desktopPlayerPrefab)
    {
        this.vrPlayerPrefab = vrPlayerPrefab;
        this.desktopPlayerPrefab = desktopPlayerPrefab;

        PlayerSpawned = false;
    }

    /// SpawnPlayer does not prevent calls if the player is already spawned, and will instead destroy the player
    public void SpawnPlayer(Vector3? position = null, Quaternion? rotation = null)
    {
        Vector3 spawnPos = position ?? Vector3.zero;
        Quaternion spawnRot = rotation ?? Quaternion.identity;

        if (activePlayerInstance != null)
        {
            Destroy(activePlayerInstance);
            activePlayerInstance = null;
            activeUI = null;
            PlayerSpawned = false;
        }

        GameObject prefabToSpawn = AppManager.Instance.Session.IsVRMode ? vrPlayerPrefab : desktopPlayerPrefab;
        GameObject newPlayer = Instantiate(prefabToSpawn, spawnPos, spawnRot);

        activePlayerInstance = newPlayer;
        activeUI = newPlayer.GetComponentInChildren<PlayerUIReferences>(true);

        if (AppManager.Instance.Session.IsVRMode)
        {
            teleportationProvider = newPlayer.GetComponentInChildren<TeleportationProvider>(true);
            xrOrigin = teleportationProvider != null ? teleportationProvider.transform : newPlayer.transform;

            leftHandDevice = default(InputDevice);
            rightHandDevice = default(InputDevice);

            if (!AppManager.Instance.Settings.EnableControllerMovement)
            {
                activeUI.CController.enabled = false;
                activeUI.ContinuousMoveProvider.enabled = false;
            }
        }

        if (activeUI == null)
        {
            Debug.LogError("PlayerManager: Spawned player is missing the 'PlayerUIReferences' component!");
            Destroy(activePlayerInstance);
            activePlayerInstance = null;
            PlayerSpawned = false;
            return;
        }

        PlayerSpawned = true;

        bool isExperimental = AppManager.Instance.Settings.ExperimentalMode;

        if (activeUI.GradientImage == null)
        {
            Debug.LogError("PlayerManager: PlayerUIReferences is missing GradientImage!");
            return;
        }

        if (!isExperimental)
        {
            activeUI.GradientImage.rectTransform.anchorMin = Vector2.zero;
            activeUI.GradientImage.rectTransform.anchorMax = Vector2.one;
            activeUI.GradientImage.rectTransform.offsetMin = Vector2.zero;
            activeUI.GradientImage.rectTransform.offsetMax = Vector2.zero;
        }

        DisableBlackscreen();
    }

    // TODO: This should probably be modernized
    public void GetVRHandWorldData(out Vector3 lPos, out Vector3 lRot, out Vector3 rPos, out Vector3 rRot)
    {
        // Defaults
        lPos = Vector3.zero; lRot = Vector3.zero;
        rPos = Vector3.zero; rRot = Vector3.zero;

        // If not in VR or player not spawned, return zeros
        if (!AppManager.Instance.Session.IsVRMode || xrOrigin == null) return;

        // Ensure Devices are valid
        if (!leftHandDevice.isValid) leftHandDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (!rightHandDevice.isValid) rightHandDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        // Get Local Data (Relative to XR Rig)
        Vector3 lPosLocal = Vector3.zero; Quaternion lRotLocal = Quaternion.identity;
        Vector3 rPosLocal = Vector3.zero; Quaternion rRotLocal = Quaternion.identity;

        bool lValid = leftHandDevice.TryGetFeatureValue(CommonUsages.devicePosition, out lPosLocal) &&
                      leftHandDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out lRotLocal);

        bool rValid = rightHandDevice.TryGetFeatureValue(CommonUsages.devicePosition, out rPosLocal) &&
                      rightHandDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out rRotLocal);

        // Transform Local Data to World Space using the XR Origin
        if (lValid)
        {
            lPos = xrOrigin.TransformPoint(lPosLocal);
            lRot = (xrOrigin.rotation * lRotLocal).eulerAngles;
        }

        if (rValid)
        {
            rPos = xrOrigin.TransformPoint(rPosLocal);
            rRot = (xrOrigin.rotation * rRotLocal).eulerAngles;
        }
    }

    public void GetVRGazeWorldData(out Vector3 gazeOrigin, out Vector3 gazeDirection)
    {
        // 1. Setup the default fallback (Head Camera)
        Transform headT = activeUI != null ? activeUI.PlayerCamera.transform : null;
        gazeOrigin = headT != null ? headT.position : Vector3.zero;
        gazeDirection = headT != null ? headT.forward : Vector3.forward;

        if (!AppManager.Instance.Session.IsVRMode || xrOrigin == null) return;

        // 2. Read actual Eye Tracking from the New Input System
        if (AppManager.Instance.EyeGazePositionAction.action != null && AppManager.Instance.EyeGazeRotationAction.action != null &&
            AppManager.Instance.EyeGazePositionAction.action.enabled && AppManager.Instance.EyeGazeRotationAction.action.enabled)
        {
            // Read Position and Rotation separately
            Vector3 eyePosLocal = AppManager.Instance.EyeGazePositionAction.action.ReadValue<Vector3>();
            Quaternion eyeRotLocal = AppManager.Instance.EyeGazeRotationAction.action.ReadValue<Quaternion>();

            // If eye tracking is active, the rotation won't be identity. 
            if (eyeRotLocal != Quaternion.identity)
            {
                // Transform the local eye position into World Space using the XROrigin
                gazeOrigin = xrOrigin.TransformPoint(eyePosLocal);

                // Transform the local eye rotation into a world forward direction
                gazeDirection = xrOrigin.TransformDirection(eyeRotLocal * Vector3.forward);
            }
        }
    }

    public void ToggleMovement()
    {
        if (AppManager.Instance.Session.IsVRMode) return;
        CanMove = !CanMove;
    }

    public void ToggleLook()
    {
        if (AppManager.Instance.Session.IsVRMode) return;
        CanLook = !CanLook;
    }

    public void EnableUI()
    {
        activeUI.MainCanvas.gameObject.SetActive(true);
        activeUI.ColorCanvas.gameObject.SetActive(true);
    }

    public void DisableUI()
    {
        activeUI.MainCanvas.gameObject.SetActive(false);
        activeUI.ColorCanvas.gameObject.SetActive(false);
    }

    public void EnableBlackscreen()
    {
        EnableUI();
        activeUI.Blackscreen.gameObject.SetActive(true);
    }

    public void DisableBlackscreen()
    {
        EnableUI();
        activeUI.Blackscreen.gameObject.SetActive(false);
    }

    public void Teleport(float x, float z)
    {
        if (AppManager.Instance.Session.IsVRMode) return;

        if (activePlayerInstance == null)
        {
            Debug.LogError("PlayerManager.Teleport called but activePlayerInstance is null. Did you SpawnPlayer()?");
            return;
        }

        Vector3 target = new Vector3(x, activePlayerInstance.transform.position.y, z);

        var cc = activePlayerInstance.GetComponent<CharacterController>();
        if (cc != null)
        {
            bool wasEnabled = cc.enabled;
            cc.enabled = false;
            activePlayerInstance.transform.position = target;
            cc.enabled = wasEnabled;
            return;
        }

        // Fallback
        activePlayerInstance.transform.position = target;
    }

    public Transform CameraPosition()
    {
        return activeUI != null ? activeUI.PlayerCamera.transform : null;
    }

    public void TeleportVRToCoordinates(float x, float z, bool alignRotation = false)
    {
        if (teleportationProvider == null || activeUI.PlayerCamera == null || xrOrigin == null)
        {
            Debug.LogError("TeleportVRToCoordinates: Missing dependencies.");
            return;
        }

        Transform headCamera = activeUI.PlayerCamera.transform;

        Quaternion targetRigRotation = xrOrigin.rotation;
        MatchOrientation matchMode = MatchOrientation.None;

        if (alignRotation)
        {
            float headYaw = headCamera.eulerAngles.y;
            float rigYaw = xrOrigin.eulerAngles.y;
            float yawDifference = headYaw - rigYaw;

            targetRigRotation = Quaternion.Euler(0, -yawDifference, 0);
            matchMode = MatchOrientation.WorldSpaceUp;
        }

        Vector3 targetWorldPos = new Vector3(x, xrOrigin.position.y, z);

        Vector3 localHeadOffset = xrOrigin.InverseTransformPoint(headCamera.position);
        localHeadOffset.y = 0;

        Vector3 futureWorldOffset = targetRigRotation * localHeadOffset;

        Vector3 newRigPos = targetWorldPos - futureWorldOffset;

        TeleportRequest request = new TeleportRequest()
        {
            destinationPosition = newRigPos,
            destinationRotation = targetRigRotation,
            matchOrientation = matchMode
        };

        teleportationProvider.QueueTeleportRequest(request);

        Debug.Log($"Recenter Triggered: Head to {x},{z} | Rotation Aligned: {alignRotation}");
    }

    public void UpdateStimulusUI(bool updateText = true)
    {
        if (!PlayerSpawned || activeUI == null) return;

        StimulusIntensity = AppManager.Instance.Stimulus.GetIntensity(activeUI.PlayerCamera.transform.position);

        if (activeUI.GradientImage != null)
        {
            Color c = new Color(StimulusIntensity, StimulusIntensity, StimulusIntensity, 1f);
            activeUI.GradientImage.color = c;
        }

        if (!AppManager.Instance.Settings.ExperimentalMode || !updateText) return;
        if (activeUI.UIText != null && activeUI.UIText.gameObject.activeSelf)
        {
            int intensity255 = Mathf.RoundToInt(StimulusIntensity * 255f);
            SetUIMessage($"Intensity: {StimulusIntensity:F3} ({intensity255})", Color.white, -1);
        }
    }

    public void ResizeTextWindow(Vector3 position, Vector2 size)
    {
        if (activeUI == null || activeUI.UIText == null)
        {
            Debug.LogWarning("UIText reference is missing.");
            return;
        }

        activeUI.UIText.rectTransform.localPosition = position;
        activeUI.UIText.rectTransform.sizeDelta = size;
    }

    public void SetUIMessage(string error, Color? color = null, float errorTimeSeconds = 5f)
    {
        activeUI.UIText.color = color ?? Color.red;
        activeUI.UIText.text = error ?? "";

        if (clearUITextCoroutine != null)
            StopCoroutine(clearUITextCoroutine);

        if (errorTimeSeconds > 0f)
            clearUITextCoroutine = StartCoroutine(ClearUITextAfterSeconds(errorTimeSeconds));
    }

    private IEnumerator ClearUITextAfterSeconds(float seconds)
    {
        yield return new WaitForSeconds(seconds);

        if (activeUI.UIText != null)
            activeUI.UIText.text = "";

        clearUITextCoroutine = null;
    }

    public void SetVRStaticUIMessage(string text)
    {
        if (!AppManager.Instance.Session.IsVRMode) { 
            Debug.LogWarning("Not in VR; SetVRStaticUIMessage should not be called because the desktop player does not have an assigned static text element.");
            return;
        }

        activeUI.StaticText.text = text ?? "";
    }
}