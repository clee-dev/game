using UnityEngine;

/// <summary>
/// First-person mouse look.
/// Mouse look is DISABLED by default -- it activates when the game starts (GameEvents.OnGameStarted).
/// ESC (pause) disables it. Resume re-enables it.
/// This means players can walk around the lobby without cursor lock fighting the UI.
/// </summary>
public class PlayerCamera : MonoBehaviour
{
    [Header("Sensitivity")]
    [Tooltip("Mouse look sensitivity.")]
    [SerializeField] private float mouseSensitivity = 0.1f;

    [Header("References")]
    [SerializeField] private Transform playerBody;

    private InputReader _input;
    private float       _xRotation;
    private bool        _mouseLookActive;

    private void Awake()
    {
        _input = GetComponentInParent<InputReader>();
    }

    private void OnEnable()
    {
        GameEvents.OnGameStarted += EnableMouseLook;
        GameEvents.OnGamePaused  += DisableMouseLook;
        GameEvents.OnGameResumed += EnableMouseLook;
    }

    private void OnDisable()
    {
        GameEvents.OnGameStarted -= EnableMouseLook;
        GameEvents.OnGamePaused  -= DisableMouseLook;
        GameEvents.OnGameResumed -= EnableMouseLook;
    }

    private void Update()
    {
        if (!_mouseLookActive || _input == null) return;

        Vector2 look = _input.LookInput * mouseSensitivity;

        _xRotation -= look.y;
        _xRotation  = Mathf.Clamp(_xRotation, -85f, 85f);

        transform.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);
        playerBody.Rotate(Vector3.up * look.x, Space.World);
    }

    private void EnableMouseLook()
    {
        _mouseLookActive = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    private void DisableMouseLook()
    {
        _mouseLookActive = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }
}