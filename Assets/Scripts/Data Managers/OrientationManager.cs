using System.Collections;
using UnityEngine;

public class OrientationManager : MonoBehaviour
{
    public bool locationOriented { get; private set; } = true;
    public bool rotationOriented { get; private set; } = true;
    private GameObject lookObjectPrefab;
    private GameObject walkObjectPrefab;

    private float lookRequiredAngleDegrees = 10f;
    private float walkRequiredProximityMeters = 0.6f;
    private float lookHoldDelaySeconds = 0.15f;
    private float walkHoldDelaySeconds = 0.5f;
    private float lookMarkerHeight = 1.5f;
    private float walkMarkerHeight = 1.25f;

    private GameObject markerInstance;

    public void Init(GameObject lookObjectPrefab, GameObject walkObjectPrefab)
    {
        this.lookObjectPrefab = lookObjectPrefab;
        this.walkObjectPrefab = walkObjectPrefab;
    }   

    public IEnumerator WalkToLocation(float x, float z)
    {
        AppManager.Instance.Player.EnableBlackscreen();
        AppManager.Instance.Player.SetUIMessage("Walk to the red waypoint", Color.white, -1);

        SpawnMarker(walkObjectPrefab, new Vector3(x, walkMarkerHeight, z));

        yield return new WaitUntil(IsPlayerCloseToMarker);

        yield return new WaitForSeconds(walkHoldDelaySeconds);

        CleanupMarker();
        AppManager.Instance.Player.SetUIMessage("", Color.white, -1);
        AppManager.Instance.Player.DisableBlackscreen();
    }

    public IEnumerator LookAtLocation(float x, float z)
    {
        AppManager.Instance.Player.EnableBlackscreen();
        AppManager.Instance.Player.SetUIMessage("Look at the red pillar", Color.white, -1);

        SpawnMarker(lookObjectPrefab, new Vector3(x, lookMarkerHeight, z));

        yield return new WaitUntil(IsPlayerLookingAtMarker);
        yield return new WaitForSeconds(lookHoldDelaySeconds);

        CleanupMarker();
        AppManager.Instance.Player.SetUIMessage("", Color.white, -1);
        AppManager.Instance.Player.DisableBlackscreen();
    }

    private void SpawnMarker(GameObject prefab, Vector3 position)
    {
        CleanupMarker();

        if (prefab == null)
        {
            Debug.LogError("VROrienter: Marker prefab is null.");
            return;
        }

        markerInstance = Instantiate(prefab, position, Quaternion.identity);
        markerInstance.layer = LayerMask.NameToLayer("VROrient");
    }

    private bool IsPlayerLookingAtMarker()
    {
        if (markerInstance == null) return false;

        Vector3 toMarker = markerInstance.transform.position - AppManager.Instance.Player.CameraPosition().position;
        float angle = Vector3.Angle(AppManager.Instance.Player.CameraPosition().forward, toMarker);
        return angle <= lookRequiredAngleDegrees;
    }

    private bool IsPlayerCloseToMarker()
    {
        if (markerInstance == null) return false;

        Vector3 playerPos = new Vector3(AppManager.Instance.Player.CameraPosition().position.x, 0f, AppManager.Instance.Player.CameraPosition().position.z);
        Vector3 markerPos = new Vector3(markerInstance.transform.position.x, 0f, markerInstance.transform.position.z);

        float dist = Vector3.Distance(playerPos, markerPos);
        return dist <= walkRequiredProximityMeters;
    }

    private void CleanupMarker()
    {
        if (markerInstance != null)
        {
            Destroy(markerInstance);
            markerInstance = null;
        }
    }
}
