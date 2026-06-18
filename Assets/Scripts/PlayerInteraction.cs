using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles all "look at and press E" interaction for the local player: picking up
/// loose items, placing a held material on a compatible build tile, hold-to-build
/// when holding the matching tool, and dropping/throwing. Add to the Player prefab
/// alongside PlayerController.
///
/// Requires a HoldPoint child Transform on the camera:
///   Main Camera
///   └── HoldPoint (local position: 0, -0.2, 1.5)
///
/// Targeting is a single forward raycast from playerCamera (Systems Architecture,
/// Section 4.2) -- replaces the old OverlapSphere "nearest in range" pickup.
///
/// Only runs logic for the local owner. Remote players' components are inactive.
/// </summary>
public class PlayerInteraction : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Camera          playerCamera;
    [SerializeField] private Transform       holdPoint;      // Child of camera, where held object sits
    [SerializeField] private TextMeshProUGUI interactPrompt; // optional, screen-space

    [Header("Settings")]
    [SerializeField] private float interactRange = 2.5f;
    [SerializeField] private float throwForce    = 8f;

    private InputReader   _input;
    private PhysicsPickup _heldObject;

    private void Awake()
    {
        _input = GetComponent<InputReader>();
    }

    private void Update()
    {
        if (!IsOwner) return;

        MoveHeldObject();

        Physics.Raycast(new Ray(playerCamera.transform.position, playerCamera.transform.forward),
            out RaycastHit hit, interactRange);
        Collider hitCollider = hit.collider;

        BuildTile     tileTarget   = hitCollider != null ? hitCollider.GetComponentInParent<BuildTile>()    : null;
        PhysicsPickup pickupTarget = hitCollider != null ? hitCollider.GetComponentInParent<PhysicsPickup>() : null;
        OrderStation  orderTarget  = hitCollider != null ? hitCollider.GetComponentInParent<OrderStation>()  : null;
        _isTargetingInteractable = tileTarget != null || pickupTarget != null || orderTarget != null;

        UpdatePrompt(tileTarget, orderTarget);
        HandleContinuousBuild(tileTarget);
        HandleInteractPress(tileTarget, pickupTarget, orderTarget);
    }

    // -------------------------------------------------------------------------
    // Per-frame
    // -------------------------------------------------------------------------

    private void MoveHeldObject()
    {
        if (_heldObject == null) return;

        // Snap the held object to the hold point each frame.
        // Since we own the object, ClientNetworkTransform syncs this to other clients.
        _heldObject.transform.SetPositionAndRotation(holdPoint.position, holdPoint.rotation);
    }

    // -------------------------------------------------------------------------
    // Continuous hold-to-build (Section 6.2, step 3)
    // -------------------------------------------------------------------------

    private void HandleContinuousBuild(BuildTile target)
    {
        if (target == null || !_input.InteractHeld) return;

        var tool = _heldObject != null ? _heldObject.GetComponent<ToolItem>() : null;
        if (tool == null || !target.CanBuild(tool.Type)) return;

        target.ContinueBuildRpc(OwnerClientId, tool.Type);
    }

    // -------------------------------------------------------------------------
    // Single press: pick up / place / drop
    // -------------------------------------------------------------------------

    private void HandleInteractPress(BuildTile tileTarget, PhysicsPickup pickupTarget, OrderStation orderTarget)
    {
        if (!_input.InteractPressed) return;
        _input.ConsumeInteract();

        if (orderTarget != null)
        {
            if (!orderTarget.WouldExceedCap)
                orderTarget.PlaceOrderRpc();
            return;
        }

        if (_heldObject == null)
        {
            TryPickup(pickupTarget);
            return;
        }

        var material = _heldObject.GetComponent<MaterialItem>();
        if (material != null && tileTarget != null && tileTarget.CanAcceptMaterial(material.Type))
        {
            PlaceHeldMaterial(tileTarget);
            return;
        }

        // Holding a tool over a buildable tile: the press that starts the hold
        // shouldn't also register as "drop" -- building itself is driven by
        // HandleContinuousBuild while E stays held.
        var tool = _heldObject.GetComponent<ToolItem>();
        if (tool != null && tileTarget != null && tileTarget.CanBuild(tool.Type))
            return;

        Drop();
    }

    private void TryPickup(PhysicsPickup pickupTarget)
    {
        if (pickupTarget == null || pickupTarget.IsHeld) return;

        // Request ownership transfer from the host
        pickupTarget.RequestPickupServerRpc(OwnerClientId);
        _heldObject = pickupTarget;
    }

    private void PlaceHeldMaterial(BuildTile target)
    {
        target.PlaceMaterialRpc(_heldObject.NetworkObject);
        _heldObject = null;
    }

    private void Drop()
    {
        if (_heldObject == null) return;

        // Throw in the direction the camera is facing
        Vector3 throwVelocity = playerCamera.transform.forward * throwForce;

        _heldObject.RequestDropServerRpc(throwVelocity);
        _heldObject = null;
    }

    // -------------------------------------------------------------------------
    // Prompt
    // -------------------------------------------------------------------------

    private void UpdatePrompt(BuildTile target, OrderStation orderTarget)
    {
        if (interactPrompt == null) return;

        if (orderTarget != null)
        {
            interactPrompt.text = orderTarget.WouldExceedCap
                ? "Material cap reached"
                : $"[E] {orderTarget.DescribeOrder()}";
            return;
        }

        if (target != null && _heldObject != null)
        {
            var material = _heldObject.GetComponent<MaterialItem>();
            if (material != null && target.CanAcceptMaterial(material.Type))
            {
                interactPrompt.text = "[E] Place Material";
                return;
            }

            var tool = _heldObject.GetComponent<ToolItem>();
            if (tool != null && target.CanBuild(tool.Type))
            {
                interactPrompt.text = "[E] Hold to Build";
                return;
            }
        }

        interactPrompt.text = _heldObject != null ? "[E] Drop" : "";
    }

    // -------------------------------------------------------------------------
    // Crosshair -- a centered dot so the player can tell exactly what the
    // raycast is aimed at when targets are close together. Drawn directly via
    // OnGUI so it needs no Canvas/Image setup in the scene or prefab.
    // -------------------------------------------------------------------------

    private const float CrosshairSize = 6f;

    private bool _isTargetingInteractable;

    private void OnGUI()
    {
        if (!IsOwner) return;

        GUI.color = _isTargetingInteractable ? Color.yellow : Color.white;
        GUI.DrawTexture(new Rect(
            Screen.width  * 0.5f - CrosshairSize * 0.5f,
            Screen.height * 0.5f - CrosshairSize * 0.5f,
            CrosshairSize, CrosshairSize), Texture2D.whiteTexture);
        GUI.color = Color.white;

        DrawOrderQueue();
    }

    // -------------------------------------------------------------------------
    // Delivery queue -- top-right list of incoming orders (Systems Architecture,
    // Section 5.3). Shared across the team, not per-player, so it reads straight
    // from OrderQueueSystem's replicated list rather than tracking anything locally.
    // -------------------------------------------------------------------------

    private const float QueueWidth      = 220f;
    private const float QueueLineHeight = 20f;
    private const float QueuePadding    = 8f;

    private void DrawOrderQueue()
    {
        if (OrderQueueSystem.Instance == null) return;

        var orders = OrderQueueSystem.Instance.PendingOrders;
        if (orders.Count == 0) return;

        float height = QueuePadding * 2f + QueueLineHeight * (orders.Count + 1);
        var area = new Rect(Screen.width - QueueWidth - 10f, 10f, QueueWidth, height);

        GUI.Box(area, "Incoming Deliveries");
        for (int i = 0; i < orders.Count; i++)
        {
            var order = orders[i];
            var lineRect = new Rect(
                area.x + QueuePadding,
                area.y + QueuePadding + QueueLineHeight * (i + 1),
                QueueWidth - QueuePadding * 2f, QueueLineHeight);
            GUI.Label(lineRect, $"{order.quantity}x {order.materialType} -- {Mathf.CeilToInt(order.remainingSeconds)}s");
        }
    }

    // -------------------------------------------------------------------------
    // Cleanup
    // -------------------------------------------------------------------------

    public override void OnNetworkDespawn()
    {
        // If this player disconnects while holding something, drop it cleanly
        if (_heldObject != null && IsServer)
            _heldObject.RequestDropServerRpc(Vector3.zero);
    }
}
