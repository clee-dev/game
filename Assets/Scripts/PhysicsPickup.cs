using System;
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

    /// <summary>Fires on every machine whenever the held state changes (true = now held).</summary>
    public event Action<bool> HeldStateChanged;

    [Tooltip("How much this item slows its solo carrier down -- see PlayerController. " +
             "Shared two-player carry (TwoPersonCarry) overrides this back to no penalty.")]
    [SerializeField] private WeightClass weightClass = WeightClass.Light;

    public WeightClass Weight => weightClass;

    // Anything that clips through a gap in the level (a missing collider, a thrown item
    // that tunnels through thin geometry, etc.) just falls forever otherwise -- that's a
    // permanent, invisible slot eaten out of the material cap and a potential softlock if
    // it was a tool. -40 is well below any blueprint's playable floor.
    [Tooltip("Despawned automatically once it falls below this Y, server-side only.")]
    [SerializeField] private float fallDespawnY = -40f;

    // Optional -- only present on Heavy items that support a second carrier binding in
    // (e.g. SteelBeam). Looked up once since RequestDropServerRpc needs it on every drop.
    private TwoPersonCarry _twoPersonCarry;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _twoPersonCarry = GetComponent<TwoPersonCarry>();
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
        HeldStateChanged?.Invoke(isNowHeld);
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned) return;
        if (transform.position.y >= fallDespawnY) return;

        NetworkObject.Despawn();
    }

    /// <summary>
    /// Server-only. Lets a layered system (e.g. MaterialItem) lock this object out of
    /// being picked up again without going through the normal pickup/drop RPC flow --
    /// used while a material is sitting in the Placed state on a build tile.
    /// </summary>
    public void SetHeldServer(bool held)
    {
        if (!IsServer) return;
        _isHeld.Value = held;
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
    ///
    /// If this item is being shared (TwoPersonCarry.IsShared), the primary letting go is a
    /// handoff, not a real drop: the secondary becomes the new primary and keeps carrying it
    /// solo (PLANNED_FEATURES.md, Steel Material -- "secondary becomes primary"), so it never
    /// touches physics or _isHeld at all.
    /// </summary>
    [ServerRpc]
    public void RequestDropServerRpc(Vector3 throwVelocity)
    {
        if (_twoPersonCarry != null && _twoPersonCarry.TryHandoffToSecondary())
            return;

        _isHeld.Value = false;

        // Return ownership to host so host simulates the throw physics
        NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);

        // Apply throw velocity on the host (now the owner)
        _rb.isKinematic = false;
        _rb.linearVelocity = throwVelocity;

        Debug.Log($"[PhysicsPickup] Dropped with velocity {throwVelocity}");
    }
}
