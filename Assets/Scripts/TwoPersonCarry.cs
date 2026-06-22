using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Lets two players share carry of a Heavy item (e.g. SteelBeam) through a pair of
/// interchangeable attach points (TwoPersonCarryPoint, pointIndex 0/1). There is no fixed
/// "primary point" -- either player can grab either point. Whoever grabs first (while the
/// item is unheld) becomes the normal PhysicsPickup owner and carries solo; a second player
/// grabbing the other point afterward binds in as the shared co-carrier. Sits alongside
/// PhysicsPickup (+ MaterialItem, for materials) on the carryable prefab.
///
/// Ownership tracks whichever holder is actually driving the transform (PhysicsPickup's
/// NetworkObject owner, Authority Mode = Owner per ClientNetworkTransform) -- _holderA/
/// _holderB only track who occupies each physical point, which is independent of who
/// currently owns. If the owner releases while shared, the other holder is promoted to
/// owner instead of the item dropping (PLANNED_FEATURES.md: "no penalty once shared" /
/// "either can release independently"); if the non-owner holder releases, the owner simply
/// keeps carrying solo (and regains the weight penalty).
///
/// Carry-point math (CarryPointFor) and the shared-distance check use carrier body-root
/// transforms, never the camera: PlayerCamera's pitch is never networked for non-owner
/// clients (NetworkPlayer disables the component entirely), so a remote player's
/// holdPoint can't be trusted from any other machine. Body position + horizontal rotation
/// IS reliably replicated via ClientNetworkTransform.
/// </summary>
[RequireComponent(typeof(PhysicsPickup), typeof(NetworkObject))]
public class TwoPersonCarry : NetworkBehaviour
{
    private const ulong NoHolder = ulong.MaxValue;

    [Header("Carry point offset -- relative to each carrier's body root")]
    [SerializeField] private float forwardOffset = 0.6f;
    [SerializeField] private float heightOffset = 1.2f;

    [Header("Auto-release the non-owner carrier if they drift this far apart (server-only)")]
    [SerializeField] private float maxShareDistance = 4f;

    [Header("Solo carry physics drag -- angular damping while exactly one point is held")]
    // Default Rigidbody angularDamping (0.05) lets a freely-rotating beam swing almost
    // forever once gravity sets it dangling -- this is restored the instant carry stops being
    // solo (shared snap, drop, or despawn), so it never affects thrown/dropped behavior.
    [SerializeField] private float soloAngularDamping = 3f;

    [Header("Local axis on this prefab that runs along the item's length")]
    // Aligned to the line between carriers when shared, or to the holder's facing when
    // solo (see OrientationFor) -- Steel.prefab is scaled long on local X (root
    // m_LocalScale {1.87, 1, 1}), not the local Z a generic "forward-facing" held item
    // would assume, so this needs to be explicit rather than hardcoded to Vector3.forward.
    [SerializeField] private Vector3 carryAxisLocal = Vector3.right;

    private readonly NetworkVariable<ulong> _holderA = new(
        NoHolder, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<ulong> _holderB = new(
        NoHolder, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsShared => _holderA.Value != NoHolder && _holderB.Value != NoHolder;
    public bool IsHeldByAnyone => _holderA.Value != NoHolder || _holderB.Value != NoHolder;

    public PhysicsPickup Pickup => _pickup;
    private PhysicsPickup _pickup;
    private Rigidbody _rb;

    // Local-only kinematic body the solo-carry ConfigurableJoint is pinned to -- never
    // networked (every client builds and drives its own from the same replicated holder-body
    // math), purely a local "hand" the joint's locked linear motion follows. See
    // UpdateSoloPhysicsDrag.
    private Rigidbody _carryAnchor;
    private ConfigurableJoint _joint;
    private bool _physicsDragActive;
    private float _originalAngularDamping;
    private Transform _pointATransform;
    private Transform _pointBTransform;

    private void Awake()
    {
        _pickup = GetComponent<PhysicsPickup>();
        _rb = GetComponent<Rigidbody>();

        foreach (TwoPersonCarryPoint point in GetComponentsInChildren<TwoPersonCarryPoint>(true))
        {
            if (point.PointIndex == 0) _pointATransform = point.transform;
            else _pointBTransform = point.transform;
        }

        var anchorObject = new GameObject($"{name} (Carry Anchor)") { hideFlags = HideFlags.HideAndDontSave };
        _carryAnchor = anchorObject.AddComponent<Rigidbody>();
        _carryAnchor.isKinematic = true;
    }

    private void OnDestroy()
    {
        if (_carryAnchor != null) Destroy(_carryAnchor.gameObject);
    }

    public override void OnNetworkSpawn() => _pickup.HeldStateChanged += OnHeldStateChanged;

    public override void OnNetworkDespawn() => _pickup.HeldStateChanged -= OnHeldStateChanged;

    // Covers the owner's PhysicsPickup losing held state without going through our own
    // bind/unbind RPCs below (e.g. disconnecting while holding, or PhysicsPickup's
    // fall-despawn floor). Without this, a leftover holder slot would stay "bound" to an
    // item nobody is actually carrying anymore.
    private void OnHeldStateChanged(bool isNowHeld)
    {
        if (!IsServer || isNowHeld) return;
        _holderA.Value = NoHolder;
        _holderB.Value = NoHolder;
    }

    // FixedUpdate, not Update -- this drives a non-kinematic Rigidbody's joint anchor, and
    // setting that immediately before PhysX's own fixed timestep (rather than at whatever rate
    // Update happens to run) is what keeps the solo-drag swing smooth instead of jittery.
    private void FixedUpdate()
    {
        // Server-only: a shared carry whose two carriers have drifted too far apart auto-drops
        // the non-owner side rather than letting the item stretch toward an ever-widening
        // midpoint forever. The owner keeps carrying solo and regains the weight penalty.
        if (IsServer && IsShared)
        {
            ulong ownerId = OwnerClientId;
            ulong otherId = OtherHolder(ownerId);
            Transform ownerBody = GetCarrierBodyTransform(ownerId);
            Transform otherBody = GetCarrierBodyTransform(otherId);
            if (ownerBody != null && otherBody != null &&
                Vector3.Distance(ownerBody.position, otherBody.position) > maxShareDistance)
                ClearHolderServer(otherId);
        }

        UpdateSoloPhysicsDrag();
    }

    /// <summary>
    /// Solo carry (exactly one attach point claimed) is physically simulated instead of
    /// snapped rigid: a ConfigurableJoint pins the grabbed point to a local-only kinematic
    /// anchor that follows the holder every frame (linear motion locked, angular free), so
    /// gravity swings the unclaimed end down within reach of a second player instead of it
    /// floating perfectly level forever. Runs on every client identically (each builds its own
    /// local anchor/joint from the same replicated holder-body math -- same pattern PhysicsPickup
    /// already relies on for thrown items: physics simulates locally everywhere, the owner's
    /// ClientNetworkTransform write is what's actually authoritative). Shared carry (both points
    /// claimed) reverts to the existing rigid midpoint-average snap in
    /// PlayerInteraction.MoveHeldObject -- two carriers already fully constrain it, nothing left
    /// to swing.
    /// </summary>
    private void UpdateSoloPhysicsDrag()
    {
        ulong soloHolder = IsShared ? NoHolder : (_holderA.Value != NoHolder ? _holderA.Value : _holderB.Value);
        if (soloHolder == NoHolder)
        {
            if (_physicsDragActive) EndSoloPhysicsDrag();
            return;
        }

        Transform holderBody = GetCarrierBodyTransform(soloHolder);
        if (holderBody == null) return;

        if (!_physicsDragActive) BeginSoloPhysicsDrag(_holderA.Value == soloHolder ? 0 : 1);

        _carryAnchor.position = CarryPointFor(holderBody);
        _rb.isKinematic = false; // re-asserted every frame -- see BeginSoloPhysicsDrag remarks
    }

    private void BeginSoloPhysicsDrag(int grabbedPointIndex)
    {
        _physicsDragActive = true;
        _originalAngularDamping = _rb.angularDamping;
        _rb.angularDamping = soloAngularDamping;

        Transform grabbedPoint = grabbedPointIndex == 0 ? _pointATransform : _pointBTransform;

        _joint = gameObject.AddComponent<ConfigurableJoint>();
        _joint.connectedBody = _carryAnchor;
        _joint.autoConfigureConnectedAnchor = false;
        _joint.anchor = grabbedPoint != null ? transform.InverseTransformPoint(grabbedPoint.position) : Vector3.zero;
        _joint.connectedAnchor = Vector3.zero;
        _joint.xMotion = ConfigurableJointMotion.Locked;
        _joint.yMotion = ConfigurableJointMotion.Locked;
        _joint.zMotion = ConfigurableJointMotion.Locked;
        _joint.angularXMotion = ConfigurableJointMotion.Free;
        _joint.angularYMotion = ConfigurableJointMotion.Free;
        _joint.angularZMotion = ConfigurableJointMotion.Free;
    }

    private void EndSoloPhysicsDrag()
    {
        _physicsDragActive = false;
        if (_joint != null) Destroy(_joint);
        _joint = null;
        _rb.angularDamping = _originalAngularDamping;

        // Only force back to kinematic when promoted to shared (PlayerInteraction's rigid
        // midpoint snap expects that). An actual drop/throw is handled by PhysicsPickup's own
        // OnHeldStateChanged, which already sets isKinematic = false for the throw -- forcing
        // it true here too would race that callback and cancel the throw.
        if (_pickup.IsHeld) _rb.isKinematic = true;
    }

    public ulong HolderAt(int pointIndex) => pointIndex == 0 ? _holderA.Value : _holderB.Value;

    public bool IsHolder(ulong clientId) => _holderA.Value == clientId || _holderB.Value == clientId;

    /// <summary>The other current holder relative to clientId -- NoHolder if clientId isn't
    /// currently a holder, or if the other point is unclaimed. Used to find which other
    /// player's body transform to average/orient against.</summary>
    public ulong OtherHolder(ulong clientId)
    {
        if (_holderA.Value == clientId) return _holderB.Value;
        if (_holderB.Value == clientId) return _holderA.Value;
        return NoHolder;
    }

    /// <summary>Can clientId bind into the given point right now? False if they already
    /// hold the other point (no double-binding the same player to both slots) or that
    /// specific point is already taken -- true whether the item is currently unheld
    /// (this becomes a solo pickup) or already held by someone else (this becomes shared).</summary>
    public bool CanBind(ulong clientId, int pointIndex) =>
        !IsHolder(clientId) && HolderAt(pointIndex) == NoHolder;

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestBindRpc(ulong requesterClientId, int pointIndex)
    {
        if (!CanBind(requesterClientId, pointIndex)) return;

        bool wasUnheld = !IsHeldByAnyone;
        if (pointIndex == 0) _holderA.Value = requesterClientId;
        else _holderB.Value = requesterClientId;

        // First grab on an unheld item is a normal pickup -- claims PhysicsPickup ownership
        // same as picking up anything else. A second grab on an already-held item binds in
        // as the shared co-carrier without touching ownership at all.
        if (wasUnheld) _pickup.ClaimServer(requesterClientId);
    }

    /// <summary>For the non-owner holder of a shared carry letting go -- just frees their
    /// point, leaving the owner carrying solo. The owner's own release goes through
    /// PhysicsPickup.RequestDropServerRpc instead (drop/throw or handoff), since that needs
    /// the full ownership-transfer flow this method deliberately skips.</summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestUnbindRpc(ulong requesterClientId)
    {
        if (!IsHolder(requesterClientId) || OwnerClientId == requesterClientId) return;
        ClearHolderServer(requesterClientId);
    }

    /// <summary>Server-only. Frees whichever point clientId currently occupies, if any. Used
    /// both by RequestUnbindRpc above and by PhysicsPickup's drop path (so a full solo drop
    /// also clears the owner's own point).</summary>
    public void ClearHolderServer(ulong clientId)
    {
        if (!IsServer) return;
        if (_holderA.Value == clientId) _holderA.Value = NoHolder;
        else if (_holderB.Value == clientId) _holderB.Value = NoHolder;
    }

    /// <summary>Server-only. Called by PhysicsPickup.RequestDropServerRpc when the current
    /// owner releases while shared: the other holder becomes the new owner and keeps
    /// carrying solo, instead of the item dropping to the ground. Returns false (does
    /// nothing) when not currently shared, so the caller can fall through to a normal drop.</summary>
    public bool TryHandoffOnOwnerRelease(ulong outgoingOwnerClientId)
    {
        if (!IsServer || !IsShared) return false;

        ulong remaining = OtherHolder(outgoingOwnerClientId);
        if (remaining == NoHolder) return false;

        ClearHolderServer(outgoingOwnerClientId);
        NetworkObject.ChangeOwnership(remaining);
        return true;
    }

    /// <summary>World point a given carrier should pull this item toward -- derived from
    /// their body root transform, deliberately ignoring camera pitch (see class remarks).</summary>
    public Vector3 CarryPointFor(Transform carrierBody) =>
        carrierBody.position + carrierBody.forward * forwardOffset + Vector3.up * heightOffset;

    /// <summary>Rotation that points carryAxisLocal along a horizontal world direction.
    /// Used for both solo and shared carry instead of Quaternion.LookRotation, which would
    /// point local +Z (not necessarily this item's long axis -- see carryAxisLocal) along
    /// the direction instead.</summary>
    public Quaternion OrientationFor(Vector3 horizontalDirection) =>
        Quaternion.FromToRotation(carryAxisLocal, horizontalDirection);

    /// <summary>Body-root transform of another connected player, by client ID -- reliable
    /// to read from any machine (position + horizontal rotation replicate via
    /// ClientNetworkTransform), unlike that player's camera/holdPoint (see class remarks).
    /// Null if that client has no spawned player object right now.</summary>
    public static Transform GetCarrierBodyTransform(ulong clientId)
    {
        NetworkObject playerObj = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);
        return playerObj != null ? playerObj.transform : null;
    }
}
