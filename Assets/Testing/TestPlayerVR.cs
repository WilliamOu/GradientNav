using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TestPlayerVR : MonoBehaviour
{
    [SerializeField] private TMP_Text colorText;
    [SerializeField] private Image colorBox;
    [SerializeField] private Transform cameraTransform;

    void Update()
    {
        float intensity01 = PosToIntensity(transform.position);
        int intensity255 = Mathf.RoundToInt(intensity01 * 255f);

        if (colorText != null)
        {
            colorText.text = $"Intensity: {intensity01:F3} ({intensity255})";
        }

        Color c = new Color(intensity01, intensity01, intensity01, 1f);

        colorBox.color = c;
    }

    // Note to self: this is a TEST function. DO NOT COPY-PASTE THIS WITHOUT MODIFICATION
    // TODO: Implement position offset
    float PosToIntensity(Vector3 worldPos)
    {
        float x = worldPos.x;
        float z = worldPos.z;

        float sigmaX = 2f;
        float sigmaZ = 2f;

        float dx = x;
        float dz = z;

        float ex = (dx * dx) / (2f * sigmaX * sigmaX);
        float ez = (dz * dz) / (2f * sigmaZ * sigmaZ);

        float g = Mathf.Exp(-(ex + ez));  // peak = 1 at (0,0)

        // Safety clamp
        return Mathf.Clamp01(g);
    }
}
