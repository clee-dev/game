using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Sits alongside PhysicsPickup on every raw material prefab (e.g. the wood plank).
/// Tracks the material state machine (Section 5.1): Loose -> Held -> Placed -> Built.
/// Loose/Held mirror PhysicsPickup's own held flag; Placed/Built are driven by BuildTile.
///
/// Setup for the prefab: Rigidbody, Collider, NetworkObject, ClientNetworkTransform,
/// PhysicsPickup, this script. Set materialType in the Inspector.
/// </summary>
[RequireComponent(typeof(PhysicsPickup), typeof(NetworkObject), typeof(Rigidbody))]
public class MaterialItem : NetworkBehaviour
{
    [SerializeField] private MaterialType materialType = MaterialType.Wood;

    public MaterialType Type => materialType;
    public MaterialState State => _state.Value;

    private readonly NetworkVariable<MaterialState> _state = new(
        MaterialState.Loose, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Rigidbody _rb;
    private PhysicsPickup _pickup;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _pickup = GetComponent<PhysicsPickup>();
    }

    public override void OnNetworkSpawn()
    {
        _pickup.HeldStateChanged += OnPickupHeldChanged;
        if (IsServer) OrderQueueSystem.Instance?.RegisterMaterialSpawned();
    }

    public override void OnNetworkDespawn()
    {
        _pickup.HeldStateChanged -= OnPickupHeldChanged;
        if (IsServer) OrderQueueSystem.Instance?.RegisterMaterialDespawned();
    }

    private void OnPickupHeldChanged(bool isHeld)
    {
        if (!IsServer) return;
        // Placed/Built are owned by BuildTile -- the pickup/drop flag doesn't drive those.
        if (_state.Value == MaterialState.Placed || _state.Value == MaterialState.Built) return;

        _state.Value = isHeld ? MaterialState.Held : MaterialState.Loose;
    }

    /// <summary>Server-only. Called by BuildTile.PlaceMaterialRpc.</summary>
    public void PlaceOnTile(Vector3 position)
    {
        NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
        _rb.isKinematic = true;
        transform.position = position;
        _pickup.SetHeldServer(true); // locked out of pickup while sitting on the tile
        _state.Value = MaterialState.Placed;
    }

    /// <summary>Server-only. Called by BuildTile when the build completes.</summary>
    public void ConsumeAsBuilt()
    {
        _state.Value = MaterialState.Built;
        NetworkObject.Despawn();
    }
}
