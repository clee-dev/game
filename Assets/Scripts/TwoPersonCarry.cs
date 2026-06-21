using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Lets a second player bind into a solo-held Heavy item (e.g. SteelBeam) via a
/// dedicated attach point (TwoPersonCarryPoint), instead of the normal single-owner
/// PhysicsPickup flow. Sits alongside PhysicsPickup (+ MaterialItem, for materials) on
/// the carryable prefab.
///
/// Ownership never changes on bind -- the primary (PhysicsPickup's NetworkObject owner)
/// keeps driving the transform the whole time, same Authority Mode = Owner pattern as
/// every other pickup. Binding only sets _secondaryHolderId so PlayerInteraction knows to
/// average both carriers' positions (see MoveHeldObject) and PlayerController knows to
/// drop the solo weight penalty for both (PLANNED_FEATURES.md: "no penalty once shared").
///
/// Position averaging uses each carrier's body-root transform, not their camera/holdPoint:
/// PlayerCamera's pitch is never networked for non-owner clients (NetworkPlayer disables
/// the component entirely), so a remote player's holdPoint can't be trusted from any other
/// machine. Body position + horizontal rotation IS reliably replicated via
/// ClientNetworkTransform, so CarryPointFor derives a fixed offset from that instead.
/// </summary>
[RequireComponent(typeof(PhysicsPickup), typeof(NetworkObject))]
public class TwoPersonCarry : NetworkBehaviour
{
    private const ulong NoHolder = ulong.MaxValue;

    [Header("Carry point offset -- relative to each carrier's body root")]
    [SerializeField] private float forwardOffset = 0.6f;
    [SerializeField] private float heightOffset = 1.2f;

    private readonly NetworkVariable<ulong> _secondaryHolderId = new(
        NoHolder, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsShared => _secondaryHolderId.Value != NoHolder;
    public ulong SecondaryHolderId => _secondaryHolderId.Value;

    private PhysicsPickup _pickup;

    private void Awake() => _pickup = GetComponent<PhysicsPickup>();

    public override void OnNetworkSpawn() => _pickup.HeldStateChanged += OnPrimaryHeldChanged;

    public override void OnNetworkDespawn() => _pickup.HeldStateChanged -= OnPrimaryHeldChanged;

    // Covers the primary letting go entirely without a handoff (e.g. despawning while
    // shared) -- TryHandoffToSecondary already clears this in the normal release path, so
    // this is a no-op there. Without this, a primary disconnect would leave the secondary
    // permanently "bound" to an item nobody is actually carrying anymore.
    private void OnPrimaryHeldChanged(bool isNowHeld)
    {
        if (!IsServer || isNowHeld) return;
        _secondaryHolderId.Value = NoHolder;
    }

    public bool CanBind(ulong clientId) =>
        _pickup.IsHeld && !IsShared && NetworkObject.OwnerClientId != clientId;

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestBindSecondaryRpc(ulong requesterClientId)
    {
        if (!CanBind(requesterClientId)) return;
        _secondaryHolderId.Value = requesterClientId;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestUnbindSecondaryRpc(ulong requesterClientId)
    {
        if (_secondaryHolderId.Value != requesterClientId) return;
        _secondaryHolderId.Value = NoHolder;
    }

    /// <summary>Server-only. Called by PhysicsPickup.RequestDropServerRpc when the current
    /// owner releases while shared: the secondary becomes the new primary and keeps
    /// carrying solo, instead of the item dropping to the ground. Returns false (does
    /// nothing) when not currently shared, so the caller can fall through to a normal drop.</summary>
    public bool TryHandoffToSecondary()
    {
        if (!IsServer || !IsShared) return false;

        ulong newPrimary = _secondaryHolderId.Value;
        _secondaryHolderId.Value = NoHolder;
        NetworkObject.ChangeOwnership(newPrimary);
        return true;
    }

    /// <summary>World point a given carrier should pull this item toward -- derived from
    /// their body root transform, deliberately ignoring camera pitch (see class remarks).</summary>
    public Vector3 CarryPointFor(Transform carrierBody) =>
        carrierBody.position + carrierBody.forward * forwardOffset + Vector3.up * heightOffset;
}
