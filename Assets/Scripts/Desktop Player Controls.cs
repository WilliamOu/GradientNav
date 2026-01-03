using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DesktopPlayerControls : MonoBehaviour
{
    [SerializeField] private CharacterController controller;
    [SerializeField] private Transform cameraTransform;

    float moveSpeed = 8f;
    float mouseSensitivity = 2f;
    float gravity = -9.81f;

    float verticalVelocity;
    float cameraPitch = 0f;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        mouseSensitivity = AppManager.Instance.Settings.MouseSensitivity;
        // moveSpeed = AppManager.Instance.Settings.MoveSpeed;
    }

    void Update()
    {
        HandleMouseLook();
        HandleMovement();
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
