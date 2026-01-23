using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MapPlayer : MonoBehaviour
{
    public enum Mode { Isometric, TopDown, FirstPerson }
    public Mode CurrentMode = Mode.Isometric;
    public Mode LastBirdsEyeViewMode = Mode.Isometric;
    public bool IsZoomed;

    [Header("References")]
    [SerializeField] private CharacterController controller;
    [SerializeField] private Transform cameraTransform;

    [Header("Physics & Movement")]
    [SerializeField] private float forceOfGravity = -19.62f;
    [SerializeField] private float jumpForce = 7f;
    [SerializeField] private float shiftSpeed = 12f;
    [SerializeField] private float speed = 4f;
    float mouseSensitivity = 2f;
    float cameraPitch = 0f;

    [Header("Camera Settings")]
    [SerializeField] private float shiftCameraPanSpeed = 16f;
    [SerializeField] private float cameraPanSpeed = 6f;
    [SerializeField] private float zoomSpeed = 2f;
    [SerializeField] private float minZoom = 3f;
    [SerializeField] private float maxZoom = 400f;
    [SerializeField] private float cameraAngle = 45f;
    [SerializeField] private float zoomedFOV = 15f;
    [SerializeField] private float defaultFOV = 60f;

    // Internal State
    private float verticalVelocity = 0f;
    private float firstPersonCameraOffset = 0.65f;
    private Camera mapCamera;
    private bool isFlying = false;

    private Vector3 birdEyeViewPosition;
    private Quaternion birdEyeViewRotation;

    private Coroutine rotateCoroutine;

    // Saved state for switching views
    private Vector3 lastPlayerPosition;
    private Quaternion lastPlayerRotation;
    private Quaternion lastCameraRotation;

    private float hasJumped = 0f;

    void Start()
    {
        mapCamera = Camera.main;

        birdEyeViewPosition = new Vector3(0f, 20f, -20f);
        birdEyeViewRotation = Quaternion.Euler(cameraAngle, 0f, 0f);

        // Initialize position
        transform.position = birdEyeViewPosition;
        if (mapCamera != null)
        {
            mapCamera.transform.rotation = birdEyeViewRotation;
        }
        mouseSensitivity = AppManager.Instance.Settings.MouseSensitivity;
    }

    void Update()
    {
        if (hasJumped > 0f) { hasJumped -= Time.deltaTime; }

        HandleMovement();
        HandleInputListeners();
        HandleZoom();
        HandleMouseLook();
    }

    private void HandleMovement()
    {
        float xDirection = Input.GetAxis("Horizontal");
        float zDirection = Input.GetAxis("Vertical");

        // Edge scrolling logic (Only in BirdsEye views)
        /*if (xDirection == 0f && zDirection == 0f && CurrentMode != Mode.FirstPerson)
        {
            float mouseX = Input.mousePosition.x;
            float mouseY = Input.mousePosition.y;
            float screenWidth = Screen.width;
            float screenHeight = Screen.height;

            if (mouseX <= 0f) xDirection = -0.5f;
            else if (mouseX >= screenWidth - 1f) xDirection = 0.5f;

            if (mouseY <= 0f) zDirection = -0.5f;
            else if (mouseY >= screenHeight - 1f) zDirection = 0.5f;
        }*/

        Vector3 move = transform.right * xDirection + transform.forward * zDirection;

        // Apply Speed
        if (Input.GetKey(KeyCode.LeftShift)) { move *= shiftSpeed; }
        else { move *= speed; }

        // Mode Specific Logic
        if (CurrentMode != Mode.FirstPerson)
        {
            BirdsEyeViewSpecificMovement();
            move *= 4f; // Birds eye view is generally faster
        }
        else
        {
            MapEditViewSpecificMovement();
        }

        // Apply Vertical Velocity (Gravity/Jumping)
        move.y = verticalVelocity;

        controller.Move(move * Time.deltaTime);
    }

    void HandleMouseLook()
    {
        if (CurrentMode != Mode.FirstPerson) return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        cameraPitch -= mouseY;
        cameraPitch = Mathf.Clamp(cameraPitch, -89f, 89f);

        cameraTransform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);

        transform.Rotate(Vector3.up * mouseX);
    }

    private void HandleZoom()
    {
        if (mapCamera == null) return;

        if (Input.GetKey(KeyCode.F))
        {
            IsZoomed = true;
            mapCamera.fieldOfView = zoomedFOV;
        }
        else
        {
            IsZoomed = false;
            mapCamera.fieldOfView = defaultFOV;
        }
    }

    private void HandleInputListeners()
    {
        // Switch View Modes
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.B))
        {
            SwitchBetweenEditAndBirdsEyeViewModes();
        }

        // Other Inputs (Rotation, Center, etc)
        if (CurrentMode == Mode.Isometric || CurrentMode == Mode.TopDown)
        {
            if (Input.GetKeyDown(KeyCode.Q)) RotateCamera(45f, 0.2f);
            if (Input.GetKeyDown(KeyCode.E)) RotateCamera(-45f, 0.2f);
            if (Input.GetKeyDown(KeyCode.R)) SwitchBirdsEyeViewModes();
            if (Input.GetKeyDown(KeyCode.C)) CenterCamera();
        }
    }

    private void CenterCamera()
    {
        transform.position = birdEyeViewPosition;
    }

    private void SwitchBetweenEditAndBirdsEyeViewModes()
    {
        if (CurrentMode == Mode.FirstPerson)
        {
            SwitchToBirdsEyeView();
        }
        else
        {
            SwitchToFirstPersonView();
        }
    }

    private void SwitchToBirdsEyeView()
    {
        verticalVelocity = 0;
        CurrentMode = LastBirdsEyeViewMode;

        // Reset camera offset
        controller.enabled = false;
        mapCamera.transform.position = new Vector3(mapCamera.transform.position.x, mapCamera.transform.position.y - firstPersonCameraOffset, mapCamera.transform.position.z);
        transform.position = new Vector3(transform.position.x, transform.position.y + firstPersonCameraOffset, transform.position.z);
        controller.enabled = true;

        StartCoroutine(LerpToLastBirdEyeView(0.2f));
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void SwitchToFirstPersonView()
    {
        verticalVelocity = 0;
        LastBirdsEyeViewMode = CurrentMode;
        CurrentMode = Mode.FirstPerson;

        // Save state before switching
        lastPlayerPosition = transform.position;
        lastPlayerRotation = transform.rotation;
        lastCameraRotation = mapCamera.transform.rotation;

        float currentTilt = mapCamera.transform.localEulerAngles.x;

        if (currentTilt > 180f) currentTilt -= 360f;

        cameraPitch = currentTilt;

        // Apply camera offset
        controller.enabled = false;
        mapCamera.transform.position = new Vector3(mapCamera.transform.position.x, mapCamera.transform.position.y + firstPersonCameraOffset, mapCamera.transform.position.z);
        transform.position = new Vector3(transform.position.x, transform.position.y - firstPersonCameraOffset, transform.position.z);
        controller.enabled = true;

        isFlying = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void MapEditViewSpecificMovement()
    {
        // Jump / Fly Logic
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (hasJumped > 0f)
            {
                // Double tap space to toggle flight
                isFlying = !isFlying;
                hasJumped = 0f;
                verticalVelocity = 0;
            }
            else
            {
                hasJumped = 0.25f;
            }
        }

        if (!isFlying)
        {
            // Standard Physics
            ApplyGravity();

            if (Input.GetKey(KeyCode.Space) && controller.isGrounded)
            {
                verticalVelocity = jumpForce;
            }
        }
        else
        {
            // Flying Physics
            if (Input.GetKey(KeyCode.Space)) { verticalVelocity = speed; }
            else if (Input.GetKey(KeyCode.LeftControl)) { verticalVelocity = -speed; }
            else { verticalVelocity = 0; }
        }
    }

    private void BirdsEyeViewSpecificMovement()
    {
        controller.enabled = false;

        // Zoom in and out with scroll wheel
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        float currentZoomSpeed = Input.GetKey(KeyCode.LeftShift) ? zoomSpeed * shiftCameraPanSpeed : zoomSpeed * cameraPanSpeed;

        if (CurrentMode == Mode.TopDown)
        {
            // Top-down: Scroll modifies Y height directly
            float newCameraHeight = Mathf.Clamp(transform.position.y - scrollInput * currentZoomSpeed, minZoom, maxZoom);
            transform.position = new Vector3(transform.position.x, newCameraHeight, transform.position.z);
        }
        else
        {
            // Isometric: Scroll modifies distance from focal point
            Vector3 cameraPosition = mapCamera.transform.position;
            Vector3 cameraForward = mapCamera.transform.forward;

            // Calculate the focal point (intersection with ground plane y=0 roughly)
            float distance = cameraForward.y == 0 ? 0.0001f : cameraPosition.y / -cameraForward.y;
            Vector3 focalPoint = cameraPosition + cameraForward * distance;

            Vector3 direction = (cameraPosition - focalPoint).normalized;
            float newDistance = Mathf.Clamp(distance - scrollInput * currentZoomSpeed, minZoom, maxZoom);
            Vector3 newCameraPosition = focalPoint + direction * newDistance;

            transform.position = newCameraPosition;
        }

        controller.enabled = true;
    }

    private void SwitchBirdsEyeViewModes()
    {
        if (CurrentMode == Mode.Isometric)
        {
            LastBirdsEyeViewMode = CurrentMode;
            CurrentMode = Mode.TopDown;
            StartCoroutine(LerpBetweenBirdsEyeViews(0.2f, 90f));
        }
        else if (CurrentMode == Mode.TopDown)
        {
            LastBirdsEyeViewMode = CurrentMode;
            CurrentMode = Mode.Isometric;
            StartCoroutine(LerpBetweenBirdsEyeViews(0.2f, 45f));
        }
    }

    // --- Coroutines & Math Helpers ---

    private IEnumerator LerpBetweenBirdsEyeViews(float duration, float endRotationX)
    {
        float startRotationX = mapCamera.transform.rotation.eulerAngles.x;
        float time = 0;

        while (time < duration)
        {
            float newRotationX = Mathf.Lerp(startRotationX, endRotationX, time / duration);
            Vector3 newRotation = new Vector3(newRotationX, mapCamera.transform.rotation.eulerAngles.y, mapCamera.transform.rotation.eulerAngles.z);
            mapCamera.transform.rotation = Quaternion.Euler(newRotation);
            time += Time.deltaTime;
            yield return null;
        }

        Vector3 finalRotation = new Vector3(endRotationX, mapCamera.transform.rotation.eulerAngles.y, mapCamera.transform.rotation.eulerAngles.z);
        mapCamera.transform.rotation = Quaternion.Euler(finalRotation);
    }

    private IEnumerator LerpToLastBirdEyeView(float duration)
    {
        Vector3 startPosition = transform.position;
        Quaternion startRotation = transform.rotation;
        Quaternion startCameraRotation = mapCamera.transform.rotation;
        float time = 0;

        while (time < duration)
        {
            transform.position = Vector3.Lerp(startPosition, lastPlayerPosition, time / duration);
            transform.rotation = Quaternion.Lerp(startRotation, lastPlayerRotation, time / duration);
            mapCamera.transform.rotation = Quaternion.Lerp(startCameraRotation, lastCameraRotation, time / duration);
            time += Time.deltaTime;
            yield return null;
        }

        transform.position = lastPlayerPosition;
        transform.rotation = lastPlayerRotation;
        mapCamera.transform.rotation = lastCameraRotation;
    }

    private void RotateCamera(float angle, float duration)
    {
        if (rotateCoroutine != null) { StopCoroutine(rotateCoroutine); }

        float currentAngle = transform.rotation.eulerAngles.y;
        float targetAngle = currentAngle + angle;

        // Snap to nearest 45 degrees
        float roundedAngle = Mathf.Round(targetAngle / 45f) * 45f;
        float actualAngle = roundedAngle - currentAngle;

        rotateCoroutine = StartCoroutine(RotateAroundPoint(actualAngle, duration));
    }

    private IEnumerator RotateAroundPoint(float angle, float duration)
    {
        controller.enabled = false;

        Vector3 cameraPosition = mapCamera.transform.position;
        Vector3 cameraForward = mapCamera.transform.forward;

        Vector3 pivotPoint;
        Ray ray = new Ray(cameraPosition, cameraForward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 1000f))
        {
            pivotPoint = hit.point;
        }
        else
        {
            // Fallback if looking into the skybox
            float distance = cameraPosition.y / -cameraForward.y;
            pivotPoint = cameraPosition + cameraForward * distance;
        }

        Quaternion startRotation = transform.rotation;
        Quaternion targetRotation = Quaternion.AngleAxis(angle, Vector3.up) * startRotation;

        Vector3 initialOffset = cameraPosition - pivotPoint;

        float elapsedTime = 0f;
        while (elapsedTime < duration)
        {
            float t = elapsedTime / duration;
            float interpolatedAngle = Mathf.Lerp(0f, angle, t);

            Vector3 rotatedOffset = Quaternion.AngleAxis(interpolatedAngle, Vector3.up) * initialOffset;
            Vector3 newCameraPosition = pivotPoint + rotatedOffset;

            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, t);
            transform.position = newCameraPosition;

            elapsedTime += Time.deltaTime;
            yield return null;
        }

        transform.rotation = targetRotation;
        transform.position = pivotPoint + Quaternion.AngleAxis(angle, Vector3.up) * initialOffset;

        controller.enabled = true;
    }

    private void ApplyGravity()
    {
        if (controller.isGrounded && verticalVelocity < 0)
        {
            verticalVelocity = -2f;
        }

        verticalVelocity += forceOfGravity * Time.deltaTime;
    }
}