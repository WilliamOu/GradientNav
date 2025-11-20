using UnityEngine;
using System;

public class SettingsManager
{
    public event Action OnSettingsChanged;

    public float MasterVolume { get; private set; } = 1f;
    public bool LeftHanded { get; private set; } = false;
    public string LocomotionMode { get; private set; } = "Teleport";

    public void SetMasterVolume(float v)
    {
        MasterVolume = v;
        OnSettingsChanged?.Invoke();
    }

    public void LoadFromDisk() { /* read JSON */ }
    public void SaveToDisk() { /* write JSON */ }
}