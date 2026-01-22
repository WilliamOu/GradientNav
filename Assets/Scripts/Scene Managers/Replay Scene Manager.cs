using UnityEngine;
using System.Collections;

public class ReplaySceneManager : MonoBehaviour
{
    IEnumerator Start()
    {
        // 1. Kick off the replay
        Debug.Log("[Scene] Requesting Replay Start...");
        AppManager.Instance.Replay.BeginReplayFromFolder();

        // 2. Wait for a frame so Start/Update can run once
        yield return null;

        // 3. Hunt for the objects
        var cloud = GameObject.Find("ShadowCloud");

        if (cloud == null)
        {
            Debug.LogError("❌ FAILURE: 'ShadowCloud' GameObject not found. SpawnVisuals() likely crashed or wasn't called.");
        }
        else
        {
            // Check for NaN bug
            Vector3 pos = cloud.transform.position;
            if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z))
            {
                Debug.LogError("❌ FATAL: ShadowCloud exists but position is NaN. This confirms the Binary Offset Bug.");
            }
            else
            {
                Debug.Log($"✅ SUCCESS: ShadowCloud found at {pos}.");

                // Check if we are inside it
                var dot = cloud.transform.GetChild(0);
                if (dot != null)
                {
                    Debug.Log($"   Dot 0 Position: {dot.position}");
                    float dist = Vector3.Distance(Camera.main.transform.position, dot.position);
                    Debug.Log($"   Distance to Camera: {dist}m");

                    if (dist < 0.3f)
                        Debug.LogWarning("⚠️ WARNING: Camera is too close! You might be clipping inside the dots.");
                }
            }
        }
    }
}