using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;


public class ClosingSceneManager : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.Button ExitButton;
    
    void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        ExitButton.onClick.AddListener(CloseApplication);
    }

    public void CloseApplication()
    {
        #if UNITY_EDITOR
        // Stop play mode in the editor
        UnityEditor.EditorApplication.isPlaying = false;

        #else
        Application.Quit();
        #endif
    }
}
