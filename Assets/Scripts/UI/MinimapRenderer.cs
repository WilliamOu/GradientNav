using UnityEngine;
using UnityEngine.UI;

public class MinimapRenderer : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private RawImage mapDisplay;
    [SerializeField] private RectTransform playerIcon;
    [SerializeField] private RectTransform goalIcon;

    [Header("Config")]
    [SerializeField] private int resolution = 256;
    [SerializeField] private Gradient heatGradient;

    private Texture2D _mapTexture;
    private float _worldSizeForUI; 

    private void Awake()
    {
        _mapTexture = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
        _mapTexture.wrapMode = TextureWrapMode.Clamp;
        _mapTexture.filterMode = FilterMode.Bilinear;

        if (mapDisplay != null) mapDisplay.texture = _mapTexture;

        heatGradient = new Gradient();
        heatGradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.black, 0.0f), new GradientColorKey(Color.white, 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) }
        );
    }

    public void RefreshMinimap()
    {
        if (!AppManager.Instance.Session.IsVRMode && !AppManager.Instance.Settings.ExperimentalMode) return;

        float width = AppManager.Instance.Settings.MapWidth;
        float length = AppManager.Instance.Settings.MapLength;

        _worldSizeForUI = Mathf.Max(width, length);

        // Generate Pixels
        Color[] pixels = new Color[resolution * resolution];
        float halfSize = _worldSizeForUI / 2f;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                // Convert Pixel (x,y) -> UV (0..1) -> World (meters)
                float u = x / (float)(resolution - 1);
                float v = y / (float)(resolution - 1);

                // Map 0..1 to -HalfSize..+HalfSize (Centered on 0,0)
                float worldX = Mathf.Lerp(-halfSize, halfSize, u);
                float worldZ = Mathf.Lerp(-halfSize, halfSize, v);

                // Query the Math Strategy
                float intensity = AppManager.Instance.Stimulus.GetIntensity(new Vector3(worldX, 0, worldZ));

                // Colorize
                pixels[y * resolution + x] = heatGradient.Evaluate(intensity);
            }
        }

        _mapTexture.SetPixels(pixels);
        _mapTexture.Apply();

        // 3. Update Goal Icon (Optional)
        if (goalIcon != null)
        {
            Vector2 goalPos = AppManager.Instance.Session.GoalPosition;
            UpdateIconPosition(goalIcon, goalPos);
        }
    }

    public void ManualUpdate()
    {
        if (!AppManager.Instance.Session.IsVRMode && !AppManager.Instance.Settings.ExperimentalMode) return;
        if (playerIcon == null) return;

        Transform playerTransform = AppManager.Instance.Player.CameraPosition();

        Vector3 camPos = playerTransform.position;
        Vector2 playerXZ = new Vector2(camPos.x, camPos.z);
        UpdateIconPosition(playerIcon, playerXZ);

        float currentYaw = playerTransform.eulerAngles.y;
        float correctedYaw = -currentYaw - 45f;

        playerIcon.rotation = Quaternion.Euler(0f, 0f, correctedYaw);
    }

    private void UpdateIconPosition(RectTransform icon, Vector2 worldPos)
    {
        if (_worldSizeForUI <= 0) return;

        float halfSize = _worldSizeForUI / 2f;
        float normX = (worldPos.x + halfSize) / _worldSizeForUI;
        float normY = (worldPos.y + halfSize) / _worldSizeForUI;

        float uiWidth = mapDisplay.rectTransform.rect.width;
        float uiHeight = mapDisplay.rectTransform.rect.height;
        float uiX = (normX - 0.5f) * uiWidth;
        float uiY = (normY - 0.5f) * uiHeight;

        icon.anchoredPosition = new Vector2(uiX, uiY);
    }
}