using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Orthographic bird's-eye camera for the Level Editor (Systems Architecture, Section 11)
/// -- deliberately not first-person, since the editor's job is laying out a grid from
/// above, not walking through it (that's what Preview Mode is for). Pan with WASD or a
/// middle-mouse drag, zoom with the scroll wheel.
///
/// Setup: attach to the editor scene's Camera alongside the Camera component. No other
/// references needed.
/// </summary>
[RequireComponent(typeof(Camera))]
public class LevelEditorCamera : MonoBehaviour
{
    [SerializeField] private float panSpeed = 15f;
    [SerializeField] private float zoomSpeed = 0.02f;
    [SerializeField] private float minZoom = 3f;
    [SerializeField] private float maxZoom = 30f;

    public Camera Camera { get; private set; }

    private bool _dragging;
    private Vector3 _dragOrigin;

    private void Awake()
    {
        Camera = GetComponent<Camera>();
        Camera.orthographic = true;
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    private void Update()
    {
        HandleKeyboardPan();
        HandleMouseDragPan();
        HandleZoom();
    }

    private void HandleKeyboardPan()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        Vector3 move = Vector3.zero;
        if (keyboard.wKey.isPressed) move += Vector3.forward;
        if (keyboard.sKey.isPressed) move += Vector3.back;
        if (keyboard.aKey.isPressed) move += Vector3.left;
        if (keyboard.dKey.isPressed) move += Vector3.right;

        if (move == Vector3.zero) return;

        // Scale by zoom level so panning feels consistent whether zoomed in or out.
        transform.position += move.normalized * panSpeed * (Camera.orthographicSize / 10f) * Time.deltaTime;
    }

    private void HandleMouseDragPan()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.middleButton.wasPressedThisFrame)
        {
            _dragging = true;
            _dragOrigin = ScreenToGroundPlane(mouse.position.ReadValue());
        }
        else if (mouse.middleButton.wasReleasedThisFrame)
        {
            _dragging = false;
        }
        else if (_dragging)
        {
            Vector3 current = ScreenToGroundPlane(mouse.position.ReadValue());
            transform.position += _dragOrigin - current;
        }
    }

    private void HandleZoom()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Approximately(scroll, 0f)) return;

        Camera.orthographicSize = Mathf.Clamp(Camera.orthographicSize - scroll * zoomSpeed, minZoom, maxZoom);
    }

    /// <summary>Raycasts the given screen point against the Y=0 plane. Used for both drag-pan and tile targeting.</summary>
    public Vector3 ScreenToGroundPlane(Vector2 screenPos) => ScreenToPlane(screenPos, 0f);

    public Vector3 ScreenToPlane(Vector2 screenPos, float planeY)
    {
        Ray ray = Camera.ScreenPointToRay(screenPos);
        var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
        return plane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : transform.position;
    }
}
