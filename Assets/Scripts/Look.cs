using UnityEngine;
using UnityEngine.InputSystem;

public class Look : MonoBehaviour
{
    [Tooltip("Degrees of rotation per pixel of mouse movement.")]
    [SerializeField] float sensitivity = 0.2f;
    [SerializeField] float minPitch = -85f;
    [SerializeField] float maxPitch = 85f;

    InputAction lookAction;
    float yaw;
    float pitch;

    void Start()
    {
        lookAction = InputSystem.actions.FindAction("Look");

        // Start from the transform's current rotation instead of snapping.
        Vector3 euler = transform.localEulerAngles;
        yaw = euler.y;
        pitch = Mathf.DeltaAngle(0f, euler.x);
    }

    void Update()
    {
        Vector2 look = lookAction.ReadValue<Vector2>();
        yaw += look.x * sensitivity;
        pitch = Mathf.Clamp(pitch - look.y * sensitivity, minPitch, maxPitch);

        // Rotation is accumulated here so Cinemachine reads it after Update.
        transform.localRotation = Quaternion.Euler(pitch, yaw, 0f);
    }
}
