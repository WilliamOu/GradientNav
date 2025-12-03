using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TestPlayer : MonoBehaviour
{
    [SerializeField] private TMP_Text colorText;
    [SerializeField] private Image colorBox;
    [SerializeField] private Transform cameraTransform;

    CharacterController controller;

    float moveSpeed = 6f;
    float mouseSensitivity = 2f;
    float gravity = -9.81f;

    float verticalVelocity;
    float cameraPitch = 0f;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        HandleMouseLook();
        HandleMovement();

        float intensity01 = PosToIntensity(transform.position);
        int intensity255 = Mathf.RoundToInt(intensity01 * 255f);

        if (colorText != null)
        {
            colorText.text = $"Intensity: {intensity01:F3} ({intensity255})";
        }

        Color c = new Color(intensity01, intensity01, intensity01, 1f);

        colorBox.color = c;
    }

    // Note to self: this is a TEST function. DO NOT COPY-PASTE THIS WITHOUT MODIFICATION
    // TODO: Implement position offset
    float PosToIntensity(Vector3 worldPos)
    {
        float x = worldPos.x;
        float z = worldPos.z;

        float sigmaX = 2f;
        float sigmaZ = 2f;

        float dx = x;
        float dz = z;

        float ex = (dx * dx) / (2f * sigmaX * sigmaX);
        float ez = (dz * dz) / (2f * sigmaZ * sigmaZ);

        float g = Mathf.Exp(-(ex + ez));  // peak = 1 at (0,0)

        // Safety clamp
        return Mathf.Clamp01(g);
    }

    void HandleMouseLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        cameraPitch -= mouseY;
        cameraPitch = Mathf.Clamp(cameraPitch, -89f, 89f);

        cameraTransform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
        transform.Rotate(Vector3.up * mouseX);
    }

    void HandleMovement()
    {
        float inputX = Input.GetAxisRaw("Horizontal");
        float inputZ = Input.GetAxisRaw("Vertical");

        Vector3 move = (transform.right * inputX + transform.forward * inputZ).normalized;

        if (controller.isGrounded)
        {
            verticalVelocity = -1f;
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        Vector3 velocity = move * moveSpeed + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);
    }

}
