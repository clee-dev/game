using UnityEngine;

/// <summary>
/// Handles character movement using CharacterController.
/// Reads from InputReader -- never reads input directly.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(InputReader))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float walkSpeed   = 5f;
    [SerializeField] private float sprintSpeed = 9f;
    [SerializeField] private float jumpHeight  = 1.2f;
    [SerializeField] private float gravity     = -15f;

    private CharacterController _cc;
    private InputReader         _input;
    private float               _verticalVelocity;

    private void Awake()
    {
        _cc    = GetComponent<CharacterController>();
        _input = GetComponent<InputReader>();
    }

    private void Update()
    {
        ApplyGravity();
        HandleJump();
        Move();
    }

    private void ApplyGravity()
    {
        // When grounded, hold a small negative value so isGrounded stays reliable on slopes.
        // When airborne, accumulate gravity downward each frame.
        if (_cc.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;
        else
            _verticalVelocity += gravity * Time.deltaTime;
    }

    private void HandleJump()
    {
        if (_input.JumpPressed && _cc.isGrounded)
        {
            // Derived from kinematic equation: v = sqrt(h * -2 * g)
            _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            _input.ConsumeJump();
        }
    }

    private void Move()
    {
        Vector2 moveInput = _input.MoveInput;
        float   speed     = _input.IsSprinting ? sprintSpeed : walkSpeed;

        // Build a direction relative to where the player body is facing
        Vector3 horizontal = transform.right   * moveInput.x
                           + transform.forward * moveInput.y;

        Vector3 motion = horizontal * speed + Vector3.up * _verticalVelocity;

        _cc.Move(motion * Time.deltaTime);
    }
}
