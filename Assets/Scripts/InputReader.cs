using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Reads raw input from the Input Action Asset and exposes it to other scripts.
/// Keep input isolated here so networking can disable this component on remote players later.
/// </summary>
public class InputReader : MonoBehaviour
{
    private PlayerInputActions _actions;

    public Vector2 MoveInput  { get; private set; }
    public Vector2 LookInput  { get; private set; }
    public bool    IsSprinting { get; private set; }
    public bool    JumpPressed { get; private set; }

    private void Awake()
    {
        _actions = new PlayerInputActions();
    }

    private void OnEnable()
    {
        _actions.Player.Enable();
        _actions.Player.Jump.performed += OnJump;
    }

    private void OnDisable()
    {
        _actions.Player.Disable();
        _actions.Player.Jump.performed -= OnJump;
    }

    private void Update()
    {
        MoveInput  = _actions.Player.Move.ReadValue<Vector2>();
        LookInput  = _actions.Player.Look.ReadValue<Vector2>();
        IsSprinting = _actions.Player.Sprint.IsPressed();
    }

    private void OnJump(InputAction.CallbackContext ctx)
    {
        JumpPressed = true;
    }

    // Call this from PlayerController after consuming the jump so it doesn't fire twice
    public void ConsumeJump()
    {
        JumpPressed = false;
    }
}
