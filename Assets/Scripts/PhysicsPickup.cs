using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Attach to any physics object that players can pick up.
/// Also requires: NetworkObject, ClientNetworkTransform (Authority Mode = Owner), Rigidbody, Collider.
///
/// Ownership flow:
///   Pickup: host validates → ChangeOwnership to holding client → holder simulates locally
///   Drop:   holder calls ServerRpc → host takes ownership back → host runs physics
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(NetworkObject))]
public class PhysicsPickup : NetworkBehaviour
{
    private Rigidbody _rb;

    private readonly NetworkVariable<bool> _isHeld = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsHeld => _isHeld.Value;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    public override void OnNetworkSpawn()
    {
        _isHeld.OnValueChanged += OnHeldStateChanged;

        // Apply initial state in case we spawn into a held state
        OnHeldStateChanged(false, _isHeld.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isHeld.OnValueChanged -= OnHeldStateChanged;
    }

    private void OnHeldStateChanged(bool _, bool isNowHeld)
    {
        // Kinematic while held so physics doesn't fight the hold position.
        // Non-kinematic when free so physics runs normally.
        _rb.isKinematic = isNowHeld;
    }

    // -------------------------------------------------------------------------
    // RPCs
    // -------------------------------------------------------------------------

    /// <summary>
    /// Any client can request pickup. Host validates and transfers ownership.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestPickupServerRpc(ulong requesterClientId)
    {
        // Reject if already held by someone
        if (_isHeld.Value) return;

        _isHeld.Value = true;
        NetworkObject.ChangeOwnership(requesterClientId);

        Debug.Log($"[PhysicsPickup] Picked up by client {requesterClientId}");
    }

    /// <summary>
    /// Only the current owner (holder) can drop. Host takes ownership back and applies throw.
    /// </summary>
    [ServerRpc]
    public void RequestDropServerRpc(Vector3 throwVelocity)
    {
        _isHeld.Value = false;

        // Return ownership to host so host simulates the throw physics
        NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);

        // Apply throw velocity on the host (now the owner)
        _rb.isKinematic = false;
        _rb.linearVelocity = throwVelocity;

        Debug.Log($"[PhysicsPickup] Dropped with velocity {throwVelocity}");
    }
}
