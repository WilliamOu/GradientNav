using UnityEngine;
using System;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// TODO: Add support for safety boundaries, since those are UI elements
public class PlayerManager
{
    public static readonly List<string> MapTypes = new List<string> { "Standard", "Linear", "Inverse" };

    public bool PlayerSpawned { get; private set; }
    public float StimulusIntensity { get; private set; } = -1f;

    private GameObject vrPlayerPrefab;
    private GameObject desktopPlayerPrefab;
    private GameObject activePlayerInstance;
    // TODO: Move this into settings or a trial system
    // TODO: The Gaussian center and player spawn point also need to be logged
    private Vector2 targetPosition = Vector2.zero; 
    private PlayerUIReferences activeUI;

    public PlayerManager(GameObject vrPlayerPrefab, GameObject desktopPlayerPrefab)
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

        // Destroy old player
        if (activePlayerInstance != null)
        {
            UnityEngine.Object.Destroy(activePlayerInstance);
        }

        // Instantiate
        GameObject prefabToSpawn = (AppManager.Instance.Session.IsVRMode) ? vrPlayerPrefab : desktopPlayerPrefab;
        GameObject newPlayer = UnityEngine.Object.Instantiate(prefabToSpawn, spawnPos, spawnRot);

        activeUI = newPlayer.GetComponent<PlayerUIReferences>();

        if (activeUI == null)
        {
            Debug.LogError("PlayerManager: Spawned player is missing the 'PlayerUIReferences' component!");
            return;
        }

        PlayerSpawned = true;

        bool isExperimental = AppManager.Instance.Settings.ExperimentalMode;

        if (activeUI.IntensityGauge != null)
            activeUI.IntensityGauge.gameObject.SetActive(isExperimental);

        // RectTransform resizing
        if (isExperimental)
        {
            // Small gauge in corner
            activeUI.GradientImage.rectTransform.sizeDelta = new Vector2(200, 200);
            activeUI.GradientImage.rectTransform.anchorMin = new Vector2(1, 0); // Bottom Right
            activeUI.GradientImage.rectTransform.anchorMax = new Vector2(1, 0);
            activeUI.GradientImage.rectTransform.pivot = new Vector2(1, 0);
            activeUI.GradientImage.rectTransform.anchoredPosition = new Vector2(-20, 20); // Margin
        }
        else
        {
            // Full screen overlay
            // Setting anchors to 0-1 stretches it to fill parent
            activeUI.GradientImage.rectTransform.anchorMin = Vector2.zero;
            activeUI.GradientImage.rectTransform.anchorMax = Vector2.one;
            activeUI.GradientImage.rectTransform.offsetMin = Vector2.zero; // Left/Bottom
            activeUI.GradientImage.rectTransform.offsetMax = Vector2.zero; // Right/Top
        }
    }

    public Transform CameraPosition()
    {
        return activeUI != null ? activeUI.PlayerCamera.transform : null;
    }

    public void SetStimulusTarget(Vector2 targetPos)
    {
        targetPosition = targetPos;
    }

    // Update this to be called from AppManager.Update()
    public void UpdateStimulusUI()
    {
        if (!PlayerSpawned || activeUI == null) return;

        // Calculate
        float intensity01 = CalculateIntensity(activeUI.PlayerCamera.transform.position);

        // Update Text
        if (activeUI.IntensityGauge != null && activeUI.IntensityGauge.gameObject.activeSelf)
        {
            int intensity255 = Mathf.RoundToInt(intensity01 * 255f);
            Transform headpos = AppManager.Instance.Player.CameraPosition();
            activeUI.IntensityGauge.text = $"Intensity: {intensity01:F3} ({intensity255})\n" +
                $"Current Position:\n{headpos.position.x:F3}, {headpos.position.z:F3}\n" +
                $"Target Position:\n{targetPosition.x:F3}, {targetPosition.y:F3}";
        }

        // Update Color
        if (activeUI.GradientImage != null)
        {
            // Assuming we want white with alpha transparency, or solid gray?
            // Usually Ganzfeld is solid color changing brightness.
            Color c = new Color(intensity01, intensity01, intensity01, 1f);
            activeUI.GradientImage.color = c;
        }

        StimulusIntensity = intensity01;
    }

    private float CalculateIntensity(Vector3 worldPos)
    {
        // Get Distance from Center (0,0)
        // We ignore Y (Height) for the map logic usually, treating it as a 2D floor map
        Vector2 playerPos2D = new Vector2(worldPos.x, worldPos.z);
        float distance = Vector2.Distance(playerPos2D, targetPosition);

        // Get Settings
        // We use the smallest dimension to define the "edge" of the map
        float mapRadius = Mathf.Min(AppManager.Instance.Settings.MapWidth, AppManager.Instance.Settings.MapLength) / 2f;
        int typeIndex = AppManager.Instance.Settings.GaussianTypeIndex;

        float intensity = 0f;

        // Select Logic
        // typeIndex 0 = Standard (Gaussian)
        // typeIndex 1 = Linear (Let's treat this as Linear for now based on your list?)
        // typeIndex 2 = Inverse

        switch (typeIndex)
        {
            case 0: // Standard Gaussian
                // Sigma controls the spread
                // Rule of thumb: At distance = sigma, intensity is ~0.60. At 2*sigma, it's ~0.13.
                // Set sigma so the edge of the map (mapRadius) is 3 standard deviations (intensity ~0)
                float sigma = mapRadius;
                intensity = Mathf.Exp(-(distance * distance) / (2f * sigma * sigma));
                break;

            case 1: // Linear
                // Simple 1.0 at center, 0.0 at edge
                intensity = 1f - (distance / mapRadius);
                break;

            case 2: // Inverse (Dark at center, bright at edge)
                sigma = mapRadius / 3f;
                intensity = 1f - Mathf.Exp(-(distance * distance) / (2f * sigma * sigma));
                break;
        }

        return Mathf.Clamp01(intensity);
    }
}