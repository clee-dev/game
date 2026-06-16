using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles physics object pickup and throwing for the local player.
/// Add to the Player prefab alongside PlayerController.
///
/// Requires a HoldPoint child Transform on the camera:
///   Main Camera
///   └── HoldPoint (local position: 0, -0.2, 1.5)
///
/// Only runs logic for the local owner. Remote players' components are inactive.
/// </summary>
public class PlayerInteraction : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Transform holdPoint;  // Child of camera, where held object sits

    [Header("Settings")]
    [SerializeField] private float pickupRange = 3f;
    [SerializeField] private float throwForce  = 8f;

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
        HandleInteractInput();
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

    private void HandleInteractInput()
    {
        if (!_input.InteractPressed) return;
        _input.ConsumeInteract();

        if (_heldObject != null)
            Drop();
        else
            TryPickup();
    }

    // -------------------------------------------------------------------------
    // Pickup / Drop
    // -------------------------------------------------------------------------

    private void TryPickup()
    {
        // Find all colliders in pickup range
        var colliders = Physics.OverlapSphere(transform.position, pickupRange);

        PhysicsPickup closest     = null;
        float         closestDist = float.MaxValue;

        foreach (var col in colliders)
        {
            var pickup = col.GetComponent<PhysicsPickup>();
            if (pickup == null)  continue;  // not a pickupable object
            if (pickup.IsHeld)   continue;  // already held by someone

            float dist = Vector3.Distance(transform.position, col.transform.position);
            if (dist >= closestDist) continue;

            closestDist = dist;
            closest     = pickup;
        }

        if (closest == null) return;

        // Request ownership transfer from the host
        closest.RequestPickupServerRpc(OwnerClientId);
        _heldObject = closest;
    }

    private void Drop()
    {
        if (_heldObject == null) return;

        // Throw in the direction the camera is facing
        Vector3 throwVelocity = Camera.main.transform.forward * throwForce;

        _heldObject.RequestDropServerRpc(throwVelocity);
        _heldObject = null;
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
