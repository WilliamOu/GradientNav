using UnityEngine;

public class SafetyWallManager : MonoBehaviour
{
    public Shader wallShader;

    public GameObject wallNorth;
    public GameObject wallSouth;
    public GameObject wallEast;
    public GameObject wallWest;

    public Color warningColor = new Color(1, 0, 0, 1);
    private float wallThickness = 0.01f;

    // Internal cache
    private Renderer[] _wallRenderers;
    private MaterialPropertyBlock _propBlock;

    private int _playerPosID;
    private int _lHandPosID;
    private int _rHandPosID;

    private Material _sharedMaterial;

    void Start()
    {
        if (!AppManager.Instance.Session.IsVRMode || !AppManager.Instance.Settings.EnableSafetyWalls)
        {
            if (wallNorth) wallNorth.gameObject.SetActive(false);
            if (wallSouth) wallSouth.gameObject.SetActive(false);
            if (wallEast) wallEast.gameObject.SetActive(false);
            if (wallWest) wallWest.gameObject.SetActive(false);
            return;
        }

        _sharedMaterial = new Material(wallShader);
        _sharedMaterial.SetColor("_MainColor", warningColor);

        // Pass the two different radii settings
        _sharedMaterial.SetFloat("_Radius", AppManager.Instance.Settings.SafetyWallRevealRadius);
        _sharedMaterial.SetFloat("_HandRadius", AppManager.Instance.Settings.SafetyWallHandRevealRadius);

        _wallRenderers = new Renderer[4];
        GameObject[] walls = { wallNorth, wallSouth, wallEast, wallWest };

        for (int i = 0; i < walls.Length; i++)
        {
            if (walls[i] != null)
            {
                var r = walls[i].GetComponent<Renderer>();
                if (r != null)
                {
                    r.material = _sharedMaterial;
                    _wallRenderers[i] = r;
                }

                var c = walls[i].GetComponent<Collider>();
                if (c != null) c.enabled = false;
            }
        }

        _propBlock = new MaterialPropertyBlock();
        _playerPosID = Shader.PropertyToID("_PlayerPos");
        _lHandPosID = Shader.PropertyToID("_LHandPos");
        _rHandPosID = Shader.PropertyToID("_RHandPos");

        SetupWalls();
    }

    void SetupWalls()
    {
        if (AppManager.Instance == null || AppManager.Instance.Settings == null)
        {
            Debug.LogError("SafetyWallController: AppManager missing.");
            return;
        }

        float width = AppManager.Instance.Settings.MapWidth;
        float length = AppManager.Instance.Settings.MapLength;

        // -0.05f gap makes corners cleaner
        ConfigureWall(wallNorth, width - 0.05f, length / 2f, true, 180);
        ConfigureWall(wallSouth, width - 0.05f, -length / 2f, true, 0);
        ConfigureWall(wallEast, length - 0.05f, width / 2f, false, 0);
        ConfigureWall(wallWest, length - 0.05f, -width / 2f, false, 0);
    }

    void ConfigureWall(GameObject wall, float longDimension, float offset, bool isHorizontal, float yRotation)
    {
        if (wall == null) return;

        wall.transform.rotation = Quaternion.identity;

        Vector3 currentScale = wall.transform.localScale;
        if (isHorizontal)
            wall.transform.localScale = new Vector3(longDimension, currentScale.y, wallThickness);
        else
            wall.transform.localScale = new Vector3(wallThickness, currentScale.y, longDimension);

        Vector3 currentPos = wall.transform.position;
        if (isHorizontal)
            wall.transform.position = new Vector3(0, currentPos.y, offset);
        else
            wall.transform.position = new Vector3(offset, currentPos.y, 0);

        wall.transform.rotation = Quaternion.Euler(0, yRotation, 0);
    }

    void Update()
    {
        if (!AppManager.Instance.Session.IsVRMode) return;
        if (AppManager.Instance.Player == null) return;

        // 1. Get Head Position
        Transform camT = AppManager.Instance.Player.CameraPosition();
        Vector3 headPos = camT != null ? camT.position : Vector3.zero;

        // 2. Get Hand Positions
        Vector3 lPos, lRot, rPos, rRot;
        AppManager.Instance.Player.GetVRHandWorldData(out lPos, out lRot, out rPos, out rRot);

        // Sanity Check: Move missing hands to infinity
        if (lPos == Vector3.zero) lPos = Vector3.one * 9999f;
        if (rPos == Vector3.zero) rPos = Vector3.one * 9999f;

        // 3. Update all walls
        for (int i = 0; i < _wallRenderers.Length; i++)
        {
            if (_wallRenderers[i] != null)
            {
                _wallRenderers[i].GetPropertyBlock(_propBlock);

                _propBlock.SetVector(_playerPosID, headPos);
                _propBlock.SetVector(_lHandPosID, lPos);
                _propBlock.SetVector(_rHandPosID, rPos);

                _wallRenderers[i].SetPropertyBlock(_propBlock);
            }
        }
    }
}