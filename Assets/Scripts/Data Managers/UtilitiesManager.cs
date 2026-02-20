using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using Unity.VisualScripting;
using UnityEngine.XR;
using static UnityEngine.GraphicsBuffer;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit;

public class UtilitiesManager : MonoBehaviour
{
    public MinimapRenderer Minimap { get; private set; }
    public Map3DVisualizer MapVisualizer { get; private set; }

    public void Awake()
    {
        MapVisualizer = GetComponentInChildren<Map3DVisualizer>(true);
        Minimap = GetComponentInChildren<MinimapRenderer>(true);

        Minimap.gameObject.SetActive(false);
    }

}