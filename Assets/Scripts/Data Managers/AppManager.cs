using UnityEngine;

// Hub and spoke data manager
public class AppManager : MonoBehaviour
{
    [SerializeField] private GameObject vrPlayerPrefab;
    [SerializeField] private GameObject desktopPlayerPrefab;

    public static AppManager Instance { get; private set; }

    public SettingsManager Settings { get; private set; }
    public SessionDataManager Session { get; private set; }
    public PlayerManager Player { get; private set; }
    public LogManager Logger { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        Settings = new SettingsManager();
        Session = new SessionDataManager();
        Player = new PlayerManager(vrPlayerPrefab, desktopPlayerPrefab);
        Logger = new LogManager();
    }
}