using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MinimapRenderer : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject minimapObj;
    [SerializeField] private RawImage mapDisplay;
    [SerializeField] private RectTransform playerIcon;
    [SerializeField] private RectTransform goalIcon;

    [Header("Config")]
    [SerializeField] private int resolution = 256;
    [SerializeField] private Gradient heatGradient;

    [Header("Trace Config")]
    [Tooltip("Assign a prefab with a simple UI Image (e.g., a white square).")]
    [SerializeField] private GameObject traceSegmentPrefab;
    [Tooltip("Assign an empty RectTransform inside the minimap to hold the segments.")]
    [SerializeField] private RectTransform traceContainer;
    [SerializeField] private float traceThickness = 2f;
    [SerializeField] private float traceRecordInterval = 0.1f;
    [SerializeField] private float traceMinMoveDistance = 0.1f;

    private Texture2D _mapTexture;
    private float _worldSizeForUI;

    private Color32[] _pixels32;
    private bool _pixelsReady;

    private struct TracePoint
    {
        public Vector2 WorldPos;
        public float TimeStamp;
    }
    private List<TracePoint> _tracePoints = new List<TracePoint>();
    private List<RectTransform> _segmentPool = new List<RectTransform>();
    private float _lastTraceTime;

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

    public void ToggleMinimap(bool toggle)
    {
        minimapObj.gameObject.SetActive(toggle);
    }

    public void RefreshMinimap()
    {
        if (!AppManager.Instance.Session.IsVRMode && !AppManager.Instance.Settings.ExperimentalMode) return;

        float width = AppManager.Instance.Settings.MapWidth;
        float length = AppManager.Instance.Settings.MapLength;

        _worldSizeForUI = Mathf.Max(width, length);

        Color[] pixels = new Color[resolution * resolution];
        float halfSize = _worldSizeForUI / 2f;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float u = x / (float)(resolution - 1);
                float v = y / (float)(resolution - 1);

                float worldX = Mathf.Lerp(-halfSize, halfSize, u);
                float worldZ = Mathf.Lerp(-halfSize, halfSize, v);

                float intensity = AppManager.Instance.Stimulus.GetIntensity(new Vector3(worldX, 0, worldZ));
                pixels[y * resolution + x] = heatGradient.Evaluate(intensity);
            }
        }

        _mapTexture.SetPixels(pixels);
        _mapTexture.Apply();

        if (goalIcon != null)
        {
            Vector2 goalPos = AppManager.Instance.Session.GoalPosition;
            UpdateIconPosition(goalIcon, goalPos);
        }
        if (AppManager.Instance.Settings.ClearTraceOnNewTrial)
        {
            ClearTrace();
        }
    }

    public void RefreshMinimapFast()
    {
        if (!AppManager.Instance.Session.IsVRMode && !AppManager.Instance.Settings.ExperimentalMode) return;

        EnsurePixels();

        float width = AppManager.Instance.Settings.MapWidth;
        float length = AppManager.Instance.Settings.MapLength;
        _worldSizeForUI = Mathf.Max(width, length);

        float halfSize = _worldSizeForUI / 2f;

        for (int y = 0; y < resolution; y++)
        {
            float v = y / (float)(resolution - 1);
            float worldZ = Mathf.Lerp(-halfSize, halfSize, v);

            for (int x = 0; x < resolution; x++)
            {
                float u = x / (float)(resolution - 1);
                float worldX = Mathf.Lerp(-halfSize, halfSize, u);

                float intensity = AppManager.Instance.Stimulus.GetIntensity(new Vector3(worldX, 0, worldZ));
                Color c = heatGradient.Evaluate(intensity);
                _pixels32[y * resolution + x] = (Color32)c;
            }
        }

        _mapTexture.SetPixels32(_pixels32);
        _mapTexture.Apply(false);

        if (goalIcon != null)
        {
            Vector2 goalPos = AppManager.Instance.Session.GoalPosition;
            UpdateIconPosition(goalIcon, goalPos);
        }
    }

    private void EnsurePixels()
    {
        if (_pixelsReady && _pixels32 != null && _pixels32.Length == resolution * resolution) return;
        _pixels32 = new Color32[resolution * resolution];
        _pixelsReady = true;
    }

    public void ManualUpdate(Transform playerTransform = null)
    {
        if (playerIcon == null) return;

        playerTransform = (playerTransform == null) ? AppManager.Instance.Player.CameraPosition() : playerTransform;

        Vector3 camPos = playerTransform.position;
        Vector2 playerXZ = new Vector2(camPos.x, camPos.z);

        UpdateIconPosition(playerIcon, playerXZ);

        float currentYaw = playerTransform.eulerAngles.y;
        float correctedYaw = -currentYaw - 45f;
        playerIcon.rotation = Quaternion.Euler(0f, 0f, correctedYaw);

        float traceLength = AppManager.Instance.Settings.RecallLength;
        if (traceLength > 0f)
        {
            float currentTime = Time.time;

            // Record a new point if enough time has passed AND the player has moved
            if (currentTime - _lastTraceTime >= traceRecordInterval)
            {
                if (_tracePoints.Count == 0 || Vector2.Distance(_tracePoints[_tracePoints.Count - 1].WorldPos, playerXZ) > traceMinMoveDistance)
                {
                    _tracePoints.Add(new TracePoint { WorldPos = playerXZ, TimeStamp = currentTime });
                }
                _lastTraceTime = currentTime;
            }

            // Cull points that are older than TraceLength
            while (_tracePoints.Count > 0 && currentTime - _tracePoints[0].TimeStamp > traceLength)
            {
                _tracePoints.RemoveAt(0);
            }

            DrawTrace();
        }
        else if (_tracePoints.Count > 0)
        {
            // If TraceLength is 0, clear data and wipe the screen segments
            _tracePoints.Clear();
            DrawTrace();
        }
    }

    // Extracted the math so it can be shared between Icons and Trace segments
    private Vector2 WorldToUIPosition(Vector2 worldPos)
    {
        if (_worldSizeForUI <= 0) return Vector2.zero;

        float halfSize = _worldSizeForUI / 2f;
        float normX = (worldPos.x + halfSize) / _worldSizeForUI;
        float normY = (worldPos.y + halfSize) / _worldSizeForUI;

        float uiWidth = mapDisplay.rectTransform.rect.width;
        float uiHeight = mapDisplay.rectTransform.rect.height;
        float uiX = (normX - 0.5f) * uiWidth;
        float uiY = (normY - 0.5f) * uiHeight;

        return new Vector2(uiX, uiY);
    }

    private void UpdateIconPosition(RectTransform icon, Vector2 worldPos)
    {
        icon.anchoredPosition = WorldToUIPosition(worldPos);
    }

    private void DrawTrace()
    {
        if (traceSegmentPrefab == null || traceContainer == null) return;

        // We need 1 less segment than we have points (connecting P1->P2, P2->P3, etc)
        int requiredSegments = Mathf.Max(0, _tracePoints.Count - 1);

        // Enable needed segments, disable unused ones
        for (int i = 0; i < _segmentPool.Count; i++)
        {
            _segmentPool[i].gameObject.SetActive(i < requiredSegments);
        }

        // Instantiate new segments if the pool is too small
        while (_segmentPool.Count < requiredSegments)
        {
            GameObject newSegment = Instantiate(traceSegmentPrefab, traceContainer);
            _segmentPool.Add(newSegment.GetComponent<RectTransform>());
        }

        // Position, rotate, and scale segments to form a continuous line
        for (int i = 0; i < requiredSegments; i++)
        {
            Vector2 startPos = WorldToUIPosition(_tracePoints[i].WorldPos);
            Vector2 endPos = WorldToUIPosition(_tracePoints[i + 1].WorldPos);

            RectTransform segment = _segmentPool[i];

            // Center the segment between the two points
            segment.anchoredPosition = (startPos + endPos) / 2f;

            // Rotate to face the next point
            Vector2 dir = endPos - startPos;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            segment.localRotation = Quaternion.Euler(0, 0, angle);

            // Stretch width to match distance, set height to trace thickness
            segment.sizeDelta = new Vector2(dir.magnitude, traceThickness);
        }
    }

    public void ClearTrace()
    {
        _tracePoints.Clear();
        DrawTrace();
    }
}