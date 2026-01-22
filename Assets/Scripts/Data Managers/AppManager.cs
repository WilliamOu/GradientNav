using UnityEngine;
using UnityEngine.InputSystem;

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

    public Material shadowDotMaterial;
    public Material xriProxyMaterial;

    public static AppManager Instance { get; private set; }

    public SettingsManager Settings { get; private set; }
    public SessionDataManager Session { get; private set; }
    public PlayerManager Player { get; private set; }
    public LogManager Logger { get; private set; }
    public OrientationManager Orientation { get; private set; }
    public StimulusManager Stimulus { get; private set; }
    public TrialManager Trial { get; private set; }
    public ShadowManager Shadow { get; private set; }
    public ReplayManager Replay { get; private set; }

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
        Stimulus = gameObject.AddComponent<StimulusManager>();
        Trial = gameObject.AddComponent<TrialManager>();
        Shadow = gameObject.AddComponent<ShadowManager>();
        Replay = gameObject.AddComponent<ReplayManager>();

        Player.Init(vrPlayerPrefab, desktopPlayerPrefab);
        Orientation.Init(lookObjectPrefab, walkObjectPrefab);
        Replay.Init(shadowDotMaterial, xriProxyMaterial);
    }
}