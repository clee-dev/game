using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Preview Mode's player stand-in (Systems Architecture, Section 11) -- lets the editor
/// verify tile reachability and sightlines by walking the blueprint in first person. Fully
/// self-contained and built entirely in code via Init() (CharacterController + a child
/// Camera), deliberately not reusing PlayerController/PlayerCamera/InputReader since those
/// assume a networked Player prefab and a GameEvents-driven mouse-look activation flow that
/// doesn't apply to a standalone editor scene. Read-only -- no interaction, just movement
/// and looking around.
/// </summary>
public class LevelEditorPreviewController : MonoBehaviour
{
    private const float WalkSpeed = 5f;
    private const float MouseSensitivity = 0.15f;
    private const float Gravity = -15f;

    private LevelEditorController _owner;
    private CharacterController _cc;
    private Camera _camera;
    private float _xRotation;
    private float _verticalVelocity;

    public void Init(LevelEditorController owner)
    {
        _owner = owner;

        _cc = gameObject.AddComponent<CharacterController>();
        _cc.height = 1.8f;
        _cc.radius = 0.4f;
        _cc.center = new Vector3(0f, 0.9f, 0f);

        var camGO = new GameObject("PreviewCamera");
        camGO.transform.SetParent(transform);
        camGO.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        _camera = camGO.AddComponent<Camera>();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        HandleLook();
        HandleMove();
        HandleExit();
    }

    private void HandleLook()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 delta = mouse.delta.ReadValue() * MouseSensitivity;
        _xRotation = Mathf.Clamp(_xRotation - delta.y, -85f, 85f);
        _camera.transform.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * delta.x);
    }

    private void HandleMove()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        Vector2 input = Vector2.zero;
        if (kb.wKey.isPressed) input.y += 1f;
        if (kb.sKey.isPressed) input.y -= 1f;
        if (kb.dKey.isPressed) input.x += 1f;
        if (kb.aKey.isPressed) input.x -= 1f;

        Vector3 horizontal = (transform.right * input.x + transform.forward * input.y).normalized * WalkSpeed;

        if (_cc.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
        else _verticalVelocity += Gravity * Time.deltaTime;

        _cc.Move((horizontal + Vector3.up * _verticalVelocity) * Time.deltaTime);
    }

    private void HandleExit()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            _owner.ExitPreviewMode();
    }

    private void OnDestroy()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
