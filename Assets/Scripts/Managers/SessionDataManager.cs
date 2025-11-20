using UnityEngine;
using UnityEngine.Analytics;

public enum Gender
{
    Unspecified,
    Male,
    Female,
    NonBinary,
    Other
}

public enum GameMode
{
    Default
}

public enum SessionType
{
    Desktop,
    VR
}

public class SessionDataManager
{
    public string ParticipantName { get; private set; }
    public string ParticipantId { get; private set; }
    public Gender ParticipantGender { get; private set; }
    public GameMode CurrentGameMode { get; private set; }
    public SessionType CurrentSession { get; private set; }
    public float SessionStartTime { get; private set; }

    public void BeginSession(string participantName, string participantId, Gender participantGender, GameMode currentGameMode, SessionType currentSession)
    {
        ParticipantName = participantName;
        ParticipantId = participantId;
        ParticipantGender = participantGender;
        CurrentGameMode = currentGameMode;
        CurrentSession = currentSession;
        SessionStartTime = UnityEngine.Time.time;
    }

    public string GetCSV()
    {
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return $"{ParticipantName}_{ParticipantId}_{timestamp}.csv";
    }
}