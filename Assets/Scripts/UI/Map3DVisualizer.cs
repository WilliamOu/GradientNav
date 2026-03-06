using UnityEngine;

public class Map3DVisualizer : MonoBehaviour
{
    [Header("Visualization Config")]
    [Tooltip("How many vertices along the longest axis. Higher = smoother but heavier.")]
    public float heightMultiplier = 4.0f;
    [SerializeField] private int meshResolution = 150;

    [Header("Appearance")]
    [Tooltip("Material MUST support Vertex Colors (e.g., Particles/Standard Surface).")]
    [SerializeField] private Material meshMaterial;
    [SerializeField] private Gradient heatGradient;

    private GameObject visualizationObj;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Mesh mesh;

    private Vector3[] _vertices;
    private Color[] _colors;
    private Vector2[] _uvs;
    private int[] _triangles;

    private int _xRes;
    private int _zRes;

    private float _builtWidth = -1f;
    private float _builtLength = -1f;
    private int _builtMeshResolution = -1;

    private bool _meshBuilt;

    // Call this from your Game Manager or UI button
    public void ToggleMap(bool state)
    {
        if (state)
        {
            // If we haven't built the mesh yet (or it was destroyed), build it
            if (visualizationObj == null)
            {
                GenerateMesh();
            }

            // If we already have it, just ensure it's on and updated
            visualizationObj.SetActive(true);
            UpdateMeshGeometry();
        }
        else
        {
            if (visualizationObj != null)
            {
                visualizationObj.SetActive(false);
            }
        }
    }

    private void GenerateMesh()
    {
        // Create the GameObject that holds the mesh
        visualizationObj = new GameObject("Generated_Stimulus_Mesh");
        visualizationObj.transform.SetParent(this.transform, false);

        // Add components
        meshFilter = visualizationObj.AddComponent<MeshFilter>();
        meshRenderer = visualizationObj.AddComponent<MeshRenderer>();

        // Assign Material (Critical for seeing colors)
        if (meshMaterial != null)
            meshRenderer.material = meshMaterial;
        else
            // Fallback to a default that usually supports vertex colors
            meshRenderer.material = new Material(Shader.Find("Particles/Standard Surface"));

        // Initialize mesh
        mesh = new Mesh();
        mesh.name = "StimulusHeightMap";
        // Utilizing 32-bit index buffer allows for meshes > 65k vertices
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        meshFilter.mesh = mesh;

        // Build the geometry
        UpdateMeshGeometry();
    }

    public void UpdateMeshGeometry(float mapWidth = -1, float mapLength = -1)
    {
        EnsureMeshBuilt(mapWidth, mapLength);
        UpdateMeshValuesOnly();
    }

    public void EnsureMeshBuilt(float mapWidth = -1, float mapLength = -1)
    {
        if (mesh == null) return;

        mapWidth = (mapWidth == -1) ? AppManager.Instance.Settings.MapWidth : mapWidth;
        mapLength = (mapLength == -1) ? AppManager.Instance.Settings.MapLength : mapLength;

        // Compute xRes/zRes exactly like you do now
        float aspectRatio = mapWidth / mapLength;

        int xRes, zRes;
        if (mapWidth >= mapLength)
        {
            xRes = meshResolution;
            zRes = Mathf.RoundToInt(meshResolution / aspectRatio);
        }
        else
        {
            zRes = meshResolution;
            xRes = Mathf.RoundToInt(meshResolution * aspectRatio);
        }

        bool needsRebuild =
            !_meshBuilt ||
            xRes != _xRes ||
            zRes != _zRes ||
            !Mathf.Approximately(mapWidth, _builtWidth) ||
            !Mathf.Approximately(mapLength, _builtLength) ||
            meshResolution != _builtMeshResolution;

        if (!needsRebuild) return;

        _xRes = xRes;
        _zRes = zRes;
        _builtWidth = mapWidth;
        _builtLength = mapLength;
        _builtMeshResolution = meshResolution;

        int vertCount = (xRes + 1) * (zRes + 1);
        _vertices = new Vector3[vertCount];
        _colors = new Color[vertCount];
        _uvs = new Vector2[vertCount];

        float halfWidth = mapWidth / 2f;
        float halfLength = mapLength / 2f;

        for (int z = 0; z <= zRes; z++)
        {
            for (int x = 0; x <= xRes; x++)
            {
                int i = z * (xRes + 1) + x;

                float u = x / (float)xRes;
                float v = z / (float)zRes;

                float worldX = Mathf.Lerp(-halfWidth, halfWidth, u);
                float worldZ = Mathf.Lerp(-halfLength, halfLength, v);

                _vertices[i] = new Vector3(worldX, 0f, worldZ); // y updated later
                _uvs[i] = new Vector2(u, v);
                _colors[i] = Color.black;
            }
        }

        _triangles = new int[xRes * zRes * 6];
        int triIndex = 0;

        for (int z = 0; z < zRes; z++)
        {
            for (int x = 0; x < xRes; x++)
            {
                int i = z * (xRes + 1) + x;

                int bl = i;
                int br = i + 1;
                int tl = i + (xRes + 1);
                int tr = i + (xRes + 1) + 1;

                _triangles[triIndex++] = bl;
                _triangles[triIndex++] = tl;
                _triangles[triIndex++] = br;

                _triangles[triIndex++] = br;
                _triangles[triIndex++] = tl;
                _triangles[triIndex++] = tr;
            }
        }

        // Apply static parts once
        mesh.Clear();
        mesh.vertices = _vertices;
        mesh.uv = _uvs;
        mesh.triangles = _triangles;
        mesh.colors = _colors;

        // Do normals once at build time (or not at all if using unlit)
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        _meshBuilt = true;
    }

    public void UpdateMeshValuesOnly()
    {
        if (mesh == null || !_meshBuilt || _vertices == null) return;

        for (int i = 0; i < _vertices.Length; i++)
        {
            float worldX = _vertices[i].x;
            float worldZ = _vertices[i].z;

            float intensity = AppManager.Instance.Stimulus.GetIntensity(new Vector3(worldX, 0, worldZ));
            _vertices[i].y = intensity * heightMultiplier;
            _colors[i] = heatGradient.Evaluate(intensity);
        }

        mesh.vertices = _vertices;
        mesh.colors = _colors;

        // For dynamic mode, skip normals. If you must have them, update rarely.
        // _mesh.RecalculateNormals();

        // Bounds can be skipped too if you set generous bounds once.
        mesh.RecalculateBounds();
    }
}