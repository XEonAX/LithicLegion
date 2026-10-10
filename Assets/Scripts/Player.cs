using UnityEngine;
using UnityEngine.InputSystem;

public class Player : MonoBehaviour
{
    [SerializeField] float torque = 10f;

    Camera cam;
    Rigidbody rb; 
    InputAction moveAction;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        cam = Camera.main;
        moveAction = InputSystem.actions.FindAction("Move");
    }

    // Update is called once per frame
    void Update()
    {

    }
    void FixedUpdate()
    {
        Vector2 input = moveAction.ReadValue<Vector2>();

        // Direction of travel relative to the camera, flattened onto the ground plane.
        Vector3 direction = new Vector3(input.x, 0f, input.y);
        direction = cam.transform.TransformDirection(direction);
        direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;

        // A sphere rolls about an axis perpendicular to where it's going, so the roll
        // axis is "up x direction". 
        Vector3 rollAxis = Vector3.Cross(Vector3.up, direction);
        rb.AddTorque(rollAxis * torque);
    }
}
