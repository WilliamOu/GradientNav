using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TestPlayerVR : MonoBehaviour
{
    [SerializeField] private TMP_Text colorText;
    [SerializeField] private Image colorBox;
    
    // 1. We specifically need the Head/Camera transform, not the Root transform
    [Tooltip("Assign the SteamVR Camera (usually labeled 'Camera' or 'Head') here")]
    [SerializeField] private Transform cameraTransform; 

    void Start()
    {
        // Fallback: If you forgot to assign it in Inspector, find the active Main Camera
        if (cameraTransform == null)
        {
            if (Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }
            else
            {
                Debug.LogError("No Camera assigned to TestPlayerVR script!");
                enabled = false;
            }
        }
    }

    void Update()
    {
        if (cameraTransform == null) return;

        // 2. Use cameraTransform.position, NOT transform.position
        float intensity01 = IntensityFunction(cameraTransform.position);
        int intensity255 = Mathf.RoundToInt(intensity01 * 255f);

        if (colorText != null)
        {
            colorText.text = $"Intensity: {intensity01:F3} ({intensity255})";
        }

        // 3. Ensure Alpha is 1 so it's actually visible
        Color c = new Color(intensity01, intensity01, intensity01, 1f);

        if (colorBox != null)
        {
            colorBox.color = c;
        }
    }

    float IntensityFunction(Vector3 worldPos)
    {
        // FIX: Use X and Z for the floor plane. Ignore Y (height).
        float distance = Vector2.Distance(new Vector2(worldPos.x, worldPos.z), Vector2.zero);

        // OPTION A: Linear (Recommended)
        // 1.0 at center, 0.0 at 2.5m away.
        // float maxDistance = 2.5f;
        // float intensity = 1f - (distance / maxDistance);

        // OPTION B: Gaussian (What you are using)
        float sigma = 1f;
        float intensity = Mathf.Exp(-(distance * distance) / (2f * sigma * sigma));

        return Mathf.Clamp01(intensity);
    }
}