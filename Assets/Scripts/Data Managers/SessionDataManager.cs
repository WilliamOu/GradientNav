using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;

// Stores and manages settings that change with each session, such as participant information
public class SessionDataManager
{
    public enum Gender { Unspecified, Male, Female, NonBinary, Other }
    public enum GameMode { Default }
    public enum SessionType { Desktop, VR }
    public enum GameState { Training, Idle, Orient, Paused, Trial } // If this is changed change the GameStateString in the LogManager too

    public int TrialNumber = -1;
    public string MapType;
    public Vector2 SpawnPosition = Vector2.zero;
    public Vector2 GoalPosition = Vector2.zero;
    public GameState State = GameState.Idle;

    // Getters enforce session start or will throw an exception
    // Getters can be found at the bottom of the script
    private string participantName; // UNUSED
    private string participantId;
    private Gender participantGender;
    private string date; // UNUSED
    private GameMode currentGameMode;
    private SessionType currentSession;
    private float sessionStartTime;

    private bool sessionStarted = false;

    private void EnsureSessionStarted()
    {
        if (!sessionStarted)
        {
            throw new InvalidOperationException("SessionDataManager: Session not started.");
        }
    }

    public void BeginSession(
        string participantId,
        Gender participantGender,
        string date,
        GameMode currentGameMode,
        SessionType currentSession)
    {
        if (sessionStarted)
        {
            Debug.LogError("The session has already been started");
            return;
        }

        this.participantId = participantId;
        this.participantGender = participantGender;
        this.date = date;
        this.currentGameMode = currentGameMode;
        this.currentSession = currentSession;
        this.sessionStartTime = Time.time;

        sessionStarted = true;
    }

    public void EndSession()
    {
        sessionStarted = false;
    }

    public string GetParticipantFolderPath()
    {
        EnsureSessionStarted();

        // 1. Root folder
        string root = Path.Combine(Application.persistentDataPath, "Participant Data Log CSVs");

        // 2. Participant ID folder
        string participantFolder = Path.Combine(root, participantId);

        // 3. Create if missing (Safe to call repeatedly)
        if (!Directory.Exists(participantFolder))
        {
            Directory.CreateDirectory(participantFolder);
        }

        return participantFolder;
    }

    public string GetCSVName()
    {
        EnsureSessionStarted();
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        // C# automatically converts Enum to string
        return $"{participantId}_{currentGameMode.ToString()}_{timestamp}.csv";
    }

    public bool IsVRMode => currentSession == SessionType.VR;

    public string ParticipantName
    {
        get
        {
            EnsureSessionStarted();
            return participantName;
        }
    }

    public string ParticipantId
    {
        get
        {
            EnsureSessionStarted();
            return participantId;
        }
    }

    public Gender ParticipantGender
    {
        get
        {
            EnsureSessionStarted();
            return participantGender;
        }
    }

    public string Date
    {
        get
        {
            EnsureSessionStarted();
            return date;
        }
    }

    public GameMode CurrentGameMode
    {
        get
        {
            EnsureSessionStarted();
            return currentGameMode;
        }
    }

    public SessionType CurrentSession
    {
        get
        {
            EnsureSessionStarted();
            return currentSession;
        }
    }

    public float SessionStartTime
    {
        get
        {
            EnsureSessionStarted();
            return sessionStartTime;
        }
    }
}
