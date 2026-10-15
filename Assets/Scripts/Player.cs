using UnityEngine;
using UnityEngine.InputSystem;

public class Player : MonoBehaviour
{
    [SerializeField] float torque = 10f;

    Camera cam;
    Rigidbody rb; 
    InputAction moveAction;

    [SerializeField] LayerMask aimLayerMask;
    [SerializeField] CrossHair crossHair;
    [SerializeField] Transform Weapons;
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
        // 2. Create the ray from the camera through the center of the screen
        Ray ray = cam.ScreenPointToRay(new Vector2(Screen.width / 2f, Screen.height / 2f));

        // 3. Cast the ray into the 3D world
        if (Physics.Raycast(ray, out RaycastHit hitInfo, Mathf.Infinity, aimLayerMask))
        {
            // Point the guns at the exact spot in 3D space the reticle is sitting on.
            Weapons.LookAt(hitInfo.point);

            ReportAimDistance(hitInfo.distance);
        }
        else
        {
            // Aiming at nothing, so the reticle reads as a target right out at range.
            ReportAimDistance(float.PositiveInfinity);
        }
    }

    /// <summary>Tells the reticle how far away the aim point is, so it can size itself to match.</summary>
    void ReportAimDistance(float distance)
    {
        if (crossHair != null) crossHair.SetDistance(distance);
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
