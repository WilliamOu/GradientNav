using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class LogManager
{
    private SessionDataManager _session;
    private PlayerManager _player;
    private List<string> _lines = new List<string>();
    private string csv;

    public LogManager(SessionDataManager session)
    {
        _session = session;
    }

    public void LogFrame()
    {
        /* var p = AppManager.Instance.Player.ActivePlayer;

        if (p == null) return; // player not spawned yet

        // TODO
        return;*/
    }

    public void WriteToDisk()
    {
        // System.IO.File.WriteAllLines(, _lines);
    }
}