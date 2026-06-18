using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Fixed-position order point (Systems Architecture, Section 5.3). Players raycast-target
/// this via PlayerInteraction and press E to order materialPrefab's type; OrderQueueSystem
/// enforces the global material cap and runs the delivery countdown. Mirrors
/// SupplyZoneSpawner/ToolDepotSpawner's convention of configuring a single material type on
/// the prefab itself rather than reading it from blueprint data.
///
/// Unlike those spawners, this needs to be a NetworkObject -- placing an order is a
/// client-to-server RPC, and RPCs live on NetworkBehaviours.
///
/// Setup: NetworkObject (registered in the NetworkManager's prefab list), a Collider for
/// PlayerInteraction's raycast to hit, this script with materialPrefab assigned (the
/// WoodPlank prefab for the MVP). deliveryPoint is optional -- leave it unassigned to have
/// deliveries land on the station itself, or point it at a separate supply zone Transform
/// per the design doc's "targetSupplyZone" field.
///
/// The portable/carryable order station (shop upgrade, "iPad item") described in the
/// design doc isn't implemented -- there's no shop system yet to unlock it from.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class OrderStation : NetworkBehaviour
{
    [SerializeField] private MaterialItem materialPrefab;
    [SerializeField] private int orderQuantity = 3;
    [SerializeField] private Transform deliveryPoint;

    public bool WouldExceedCap =>
        OrderQueueSystem.Instance.LiveMaterialCount.Value
        + OrderQueueSystem.Instance.PendingOrderedQuantity.Value
        + orderQuantity > OrderQueueSystem.Instance.MaterialCap;

    public string DescribeOrder() => $"Order {orderQuantity} {materialPrefab.Type}";

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void PlaceOrderRpc()
    {
        Vector3 position = deliveryPoint != null ? deliveryPoint.position : transform.position;
        OrderQueueSystem.Instance.TryPlaceOrder(materialPrefab, orderQuantity, position);
    }
}
