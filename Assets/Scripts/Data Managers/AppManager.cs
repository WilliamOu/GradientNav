using UnityEngine;
using UnityEngine.InputSystem;

// Hub and spoke data manager
public class AppManager : MonoBehaviour
{
    [SerializeField] private GameObject vrPlayerPrefab;
    [SerializeField] private GameObject desktopPlayerPrefab;
    [SerializeField] private GameObject lookObjectPrefab;
    [SerializeField] private GameObject walkObjectPrefab;

    public InputActionProperty LeftActivate;
    public InputActionProperty RightActivate;
    public InputActionProperty LeftSelect;
    public InputActionProperty RightSelect;

    public static AppManager Instance { get; private set; }

    public SettingsManager Settings { get; private set; }
    public SessionDataManager Session { get; private set; }
    public PlayerManager Player { get; private set; }
    public LogManager Logger { get; private set; }
    public OrientationManager Orientation { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        Settings = new SettingsManager();
        Session = new SessionDataManager();
        Logger = new LogManager();
        Player = gameObject.AddComponent<PlayerManager>();
        Orientation = gameObject.AddComponent<OrientationManager>();

        Player.Init(vrPlayerPrefab, desktopPlayerPrefab);
        Orientation.Init(lookObjectPrefab, walkObjectPrefab);
    }
}