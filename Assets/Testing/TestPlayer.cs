using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TestPlayer : MonoBehaviour
{
    [SerializeField] private TMP_Text colorText;
    [SerializeField] private Image colorBox;
    [SerializeField] private Transform cameraTransform;

    [SerializeField] private float maxDistance = 8f;
    [SerializeField] private bool useGaussian = true;

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
        // 1. Calculate distance from center (0,0) ignoring height (y)
        float distance = Vector2.Distance(new Vector2(worldPos.x, worldPos.z), Vector2.zero);
        // Note: If you want 3D distance including height, use Vector3.Distance(worldPos, Vector3.zero)

        if (useGaussian)
        {
            // --- OPTION A: Gaussian (Bell Curve) ---
            // If you stick with this, increase sigma to ~3.5f or 4.0f
            float sigma = 3.5f;
            float g = Mathf.Exp(-(distance * distance) / (2f * sigma * sigma));
            return Mathf.Clamp01(g);
        }
        else
        {
            // --- OPTION B: Linear (Cone) ---
            // 1.0 at center, 0.0 at maxDistance. Constant slope.
            // This is usually easier for participants to "feel" the gradient.
            float intensity = 1f - (distance / maxDistance);

            return Mathf.Clamp01(intensity);
        }
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
