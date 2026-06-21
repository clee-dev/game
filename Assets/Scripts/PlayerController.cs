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

    [Header("Weight-based speed penalty (PLANNED_FEATURES.md, Weight Classes)")]
    [SerializeField] private PlayerInteraction playerInteraction;
    [Tooltip("Placeholder -- Cameron to tune.")]
    [SerializeField] [Range(0f, 1f)] private float mediumWeightMultiplier = 0.85f;
    [Tooltip("75% movement speed reduction per PLANNED_FEATURES.md (Steel Material).")]
    [SerializeField] [Range(0f, 1f)] private float heavyWeightMultiplier = 0.25f;

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
        float   speed     = (_input.IsSprinting ? sprintSpeed : walkSpeed) * CurrentWeightMultiplier();

        // Build a direction relative to where the player body is facing
        Vector3 horizontal = transform.right   * moveInput.x
                           + transform.forward * moveInput.y;

        Vector3 motion = horizontal * speed + Vector3.up * _verticalVelocity;

        _cc.Move(motion * Time.deltaTime);
    }

    /// <summary>1.0 when empty-handed or holding a Light item. A solo Heavy/Medium carry
    /// applies its penalty; a shared two-player carry (TwoPersonCarry.IsShared) drops the
    /// penalty back to 1.0 for both carriers (PLANNED_FEATURES.md: "no penalty once
    /// shared") -- the secondary carrier never sets PlayerInteraction.HeldObject at all, so
    /// this already returns 1.0 for them without any extra check.</summary>
    private float CurrentWeightMultiplier()
    {
        PhysicsPickup held = playerInteraction != null ? playerInteraction.HeldObject : null;
        if (held == null) return 1f;

        TwoPersonCarry carry = held.GetComponent<TwoPersonCarry>();
        if (carry != null && carry.IsShared) return 1f;

        return held.Weight switch
        {
            WeightClass.Medium => mediumWeightMultiplier,
            WeightClass.Heavy  => heavyWeightMultiplier,
            _                  => 1f,
        };
    }
}
