using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using Unity.VisualScripting;
using UnityEngine.XR;
using static UnityEngine.GraphicsBuffer;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit;

public class PlayerManager : MonoBehaviour
{
    public bool PlayerSpawned { get; private set; }
    public float StimulusIntensity { get; private set; } = -1f;
    public bool CanMove { get; private set; } = true;
    public bool CanLook { get; private set; } = true;
    public MinimapRenderer Minimap { get; private set; }

    private GameObject vrPlayerPrefab;
    private GameObject desktopPlayerPrefab;
    private GameObject activePlayerInstance;
    private Coroutine clearUITextCoroutine;
    private PlayerUIReferences activeUI;
    private TeleportationProvider teleportationProvider;
    private Transform xrOrigin;
    private InputDevice leftHandDevice;
    private InputDevice rightHandDevice;
    private InputDevice eyeTrackingDevice;

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
        }

        Minimap = newPlayer.GetComponentInChildren<MinimapRenderer>(true);
        if (!AppManager.Instance.Settings.ExperimentalMode && !AppManager.Instance.Session.IsVRMode)
        {
            Minimap.gameObject.SetActive(false);
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
        // If anything fails, we assume the user is looking where the camera points.
        Transform headT = activeUI != null ? activeUI.PlayerCamera.transform : null;
        if (headT != null)
        {
            gazeOrigin = headT.position;
            gazeDirection = headT.forward;
        }
        else
        {
            gazeOrigin = Vector3.zero;
            gazeDirection = Vector3.forward;
        }

        if (!AppManager.Instance.Session.IsVRMode || xrOrigin == null) return;

        if (!eyeTrackingDevice.isValid)
        {
            // We look for a device that supports EyeTracking
            var potentialDevices = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.EyeTracking, potentialDevices);

            if (potentialDevices.Count > 0)
                eyeTrackingDevice = potentialDevices[0];
        }

        if (eyeTrackingDevice.isValid)
        {
            // Note: Some headsets provide 'centerEyePosition/Rotation', others use 'devicePosition/Rotation' for eyes
            // We check both to be safe.
            bool hasPos = eyeTrackingDevice.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 eyePosLocal)
                          || eyeTrackingDevice.TryGetFeatureValue(CommonUsages.devicePosition, out eyePosLocal);

            bool hasRot = eyeTrackingDevice.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion eyeRotLocal)
                          || eyeTrackingDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out eyeRotLocal);

            if (hasPos && hasRot)
            {
                // Transform from XR Rig Local Space to World Space
                gazeOrigin = xrOrigin.TransformPoint(eyePosLocal);

                // For Gaze, we usually care about the Direction Vector, not just the Euler angles
                Quaternion worldRot = xrOrigin.rotation * eyeRotLocal;
                gazeDirection = worldRot * Vector3.forward;
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

    public void TeleportVRToCoordinates(float x, float z)
    {
        if (teleportationProvider == null || activeUI.PlayerCamera == null || xrOrigin == null)
        {
            Debug.LogError("TeleportVRToCoordinates: Missing dependencies.");
            return;
        }

        Vector3 rigPos = xrOrigin.position;
        Vector3 headPos = activeUI.PlayerCamera.transform.position;

        Vector3 headOffsetFromRig = headPos - rigPos;
        headOffsetFromRig.y = 0; // Flatten (we don't want to mess with floor height)

        Vector3 targetWorldPos = new Vector3(x, rigPos.y, z);
        Vector3 newRigPos = targetWorldPos - headOffsetFromRig;

        TeleportRequest request = new TeleportRequest()
        {
            destinationPosition = newRigPos,
            destinationRotation = Quaternion.identity, // Resets rotation to face World Forward (Z+)
            matchOrientation = MatchOrientation.None
        };

        teleportationProvider.QueueTeleportRequest(request);

        Debug.Log($"Recenter Triggered: Moved Head to {x},{z} (Rig moved to {newRigPos})");
    }

    public void UpdateStimulusUI()
    {
        if (!PlayerSpawned || activeUI == null) return;

        StimulusIntensity = AppManager.Instance.Stimulus.GetIntensity(activeUI.PlayerCamera.transform.position);

        if (activeUI.GradientImage != null)
        {
            Color c = new Color(StimulusIntensity, StimulusIntensity, StimulusIntensity, 1f);
            activeUI.GradientImage.color = c;
        }

        if (!AppManager.Instance.Settings.ExperimentalMode) return;
        if (activeUI.UIText != null && activeUI.UIText.gameObject.activeSelf)
        {
            int intensity255 = Mathf.RoundToInt(StimulusIntensity * 255f);
            activeUI.UIText.text = $"Intensity: {StimulusIntensity:F3} ({intensity255})";
        }
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
}