using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PhotonView))]
public class PlayerMovement3D : MonoBehaviourPun, IPunObservable
{
    Animator animator;

    [Header("Movement")]
    public float walkSpeed = 6f;
    public float runSpeed = 9f;
    public float acceleration = 20f; // higher = snappier speed change
    [Tooltip("How quickly remote players interpolate to networked position")]
    public float positionLerpSpeed = 10f;
    [Tooltip("How quickly remote players interpolate to networked rotation")]
    public float rotationLerpSpeed = 10f;

    [Header("Jump & Gravity")]
    public float jumpHeight = 1.6f; // world units
    public float gravity = -9.81f;
    public float groundedStick = -2f; // small downward force to keep grounded

    [Header("Ground Check (optional)")]
    public LayerMask groundLayers;
    public Transform groundCheck;
    public float groundCheckRadius = 0.4f;

    [Header("Misc")]
    public bool toggleRunWithLeftShift = true; // hold shift to run (or toggle)
    public bool lockCursor = true;

    // internals
    CharacterController cc;
    float currentSpeed;
    Vector3 velocity; // used for vertical gravity/jump
    bool running = false;
    bool runToggled = false;

    // networked values (received for remote objects)
    Vector3 networkPosition;
    Quaternion networkRotation;
    float networkVerticalVelocity = 0f;
    bool firstNetworkUpdate = true;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        cc = GetComponent<CharacterController>();
        currentSpeed = walkSpeed;

        if (groundCheck == null)
        {
            GameObject go = new GameObject("GroundCheck");
            go.transform.SetParent(transform);
            go.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            groundCheck = go.transform;
        }

        // initialize network targets
        networkPosition = transform.position;
        networkRotation = transform.rotation;
    }

    void Start()
    {
        if (lockCursor && photonView.IsMine)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // If this object is not ours, we don't want the CharacterController to interfere with interpolation.
        if (!photonView.IsMine)
        {
            // Optionally keep CharacterController but don't use it for movement.
            // We won't call cc.Move for remote players; we'll lerp transform.
        }
    }

    void Update()
    {
        // If this is the local player's avatar, process input & physics
        if (photonView.IsMine)
        {
            HandleRunInput();
            MovePlayer();
        }
        else
        {
            // Smoothly interpolate position & rotation for remote players
            transform.position = Vector3.Lerp(transform.position, networkPosition, Time.deltaTime * positionLerpSpeed);
            transform.rotation = Quaternion.Slerp(transform.rotation, networkRotation, Time.deltaTime * rotationLerpSpeed);

            // Apply vertical smoothing to give jump/land feel (optional)
            // We don't drive the CharacterController for remote - just lerp position as above.
        }
    }

    void HandleRunInput()
    {
        if (toggleRunWithLeftShift)
        {
            running = Input.GetKey(KeyCode.LeftShift);
        }
        else
        {
            if (Input.GetKeyDown(KeyCode.LeftShift))
                runToggled = !runToggled;
            running = runToggled;
        }
    }

    bool IsGrounded()
    {
        if (cc.isGrounded) return true;
        if (groundCheck != null)
            return Physics.CheckSphere(groundCheck.position, groundCheckRadius, groundLayers, QueryTriggerInteraction.Ignore);
        return false;
    }

    void MovePlayer()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        Vector3 input = new Vector3(h, 0f, v);
        input = Vector3.ClampMagnitude(input, 1f);

        Vector3 desiredMove = transform.TransformDirection(input);

        float targetSpeed = running ? runSpeed : walkSpeed;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed * input.magnitude, acceleration * Time.deltaTime);

        Vector3 horizontal = desiredMove * currentSpeed;

        if (IsGrounded())
        {
            if (velocity.y < 0)
                velocity.y = groundedStick;

            if (Input.GetButtonDown("Jump"))
            {
                velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
        }
        else
        {
            velocity.y += gravity * Time.deltaTime;
        }

        Vector3 move = desiredMove * currentSpeed + Vector3.up * velocity.y;
        cc.Move(move * Time.deltaTime);

        float moveMagnitude = input.magnitude; // 0 to 1
animator.SetFloat("Speed", moveMagnitude, 0.1f, Time.deltaTime * 10f);
Debug.Log("Speed parameter: " + moveMagnitude);



    }

    // IPunObservable implementation: sync position, rotation, vertical velocity
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // local player -> send state
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(velocity.y);
        }
        else
        {
            // remote -> receive state
            networkPosition = (Vector3)stream.ReceiveNext();
            networkRotation = (Quaternion)stream.ReceiveNext();
            networkVerticalVelocity = (float)stream.ReceiveNext();

            if (firstNetworkUpdate)
            {
                // immediately snap on first update to avoid long lerp from origin
                transform.position = networkPosition;
                transform.rotation = networkRotation;
                firstNetworkUpdate = false;
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1.5f);
    }
}
