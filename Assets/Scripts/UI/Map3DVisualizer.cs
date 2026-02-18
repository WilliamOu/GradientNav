using UnityEngine;

public class Map3DVisualizer : MonoBehaviour
{
    [Header("Visualization Config")]
    [Tooltip("How many vertices along the longest axis. Higher = smoother but heavier.")]
    [SerializeField] private int meshResolution = 150;
    [SerializeField] private float heightMultiplier = 4.0f;

    [Header("Appearance")]
    [Tooltip("Material MUST support Vertex Colors (e.g., Particles/Standard Surface).")]
    [SerializeField] private Material meshMaterial;
    [SerializeField] private Gradient heatGradient;

    private GameObject visualizationObj;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Mesh mesh;

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
            UpdateMeshGeometry(); // Optional: Refresh in case map params changed while hidden
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

    public void UpdateMeshGeometry()
    {
        if (mesh == null) return;

        // Gather Settings
        float mapWidth = AppManager.Instance.Settings.MapWidth;
        float mapLength = AppManager.Instance.Settings.MapLength;

        // Determine step size to keep quads square-ish
        // We use the same resolution for the longest side
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

        // Generate Vertices & Colors
        Vector3[] vertices = new Vector3[(xRes + 1) * (zRes + 1)];
        Color[] colors = new Color[vertices.Length];
        Vector2[] uvs = new Vector2[vertices.Length];

        float halfWidth = mapWidth / 2f;
        float halfLength = mapLength / 2f;

        for (int z = 0; z <= zRes; z++)
        {
            for (int x = 0; x <= xRes; x++)
            {
                int i = z * (xRes + 1) + x;

                // Normalized coordinates (0 to 1)
                float u = x / (float)xRes;
                float v = z / (float)zRes;

                // World position (Centered at 0,0 like your Minimap)
                float worldX = Mathf.Lerp(-halfWidth, halfWidth, u);
                float worldZ = Mathf.Lerp(-halfLength, halfLength, v);

                // Query the Stimulus Manager
                // Note: GetIntensity usually takes Vector3 worldPos
                float intensity = AppManager.Instance.Stimulus.GetIntensity(new Vector3(worldX, 0, worldZ));

                // Apply Height
                float yPos = intensity * heightMultiplier;

                vertices[i] = new Vector3(worldX, yPos, worldZ);
                colors[i] = heatGradient.Evaluate(intensity);
                uvs[i] = new Vector2(u, v);
            }
        }

        // Generate Triangles
        int[] triangles = new int[xRes * zRes * 6];
        int triIndex = 0;

        for (int z = 0; z < zRes; z++)
        {
            for (int x = 0; x < xRes; x++)
            {
                int i = z * (xRes + 1) + x;

                // Quad vertex indices
                int bl = i;
                int br = i + 1;
                int tl = i + (xRes + 1);
                int tr = i + (xRes + 1) + 1;

                // First triangle
                triangles[triIndex] = bl;
                triangles[triIndex + 1] = tl;
                triangles[triIndex + 2] = br;

                // Second triangle
                triangles[triIndex + 3] = br;
                triangles[triIndex + 4] = tl;
                triangles[triIndex + 5] = tr;

                triIndex += 6;
            }
        }

        // Apply to Mesh
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.colors = colors;
        mesh.uv = uvs;
        mesh.triangles = triangles;

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }
}