using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Fixed-position order point (Systems Architecture, Section 5.3). Players raycast-target
/// this via PlayerInteraction, press E to open a pop-up menu listing availableMaterials,
/// then press a number key to order that material's type; OrderQueueSystem enforces the
/// global material cap and runs the delivery countdown. Mirrors SupplyZoneSpawner/
/// ToolDepotSpawner's convention of configuring material types on the prefab itself rather
/// than reading them from blueprint data, just as a list instead of a single entry.
///
/// Unlike those spawners, this needs to be a NetworkObject -- placing an order is a
/// client-to-server RPC, and RPCs live on NetworkBehaviours.
///
/// Setup: NetworkObject (registered in the NetworkManager's prefab list), a Collider for
/// PlayerInteraction's raycast to hit, this script with availableMaterials assigned (e.g.
/// the WoodPlank prefab for the MVP -- add more material prefabs here as they exist).
/// deliveryPoint is optional -- leave it unassigned to have deliveries land at the
/// SupplyZoneSpawner in the scene whose materialPrefab matches the ordered type (the design
/// doc's "targetSupplyZone" field), or assign a Transform here to override that and send
/// every delivery from this station somewhere else entirely.
///
/// The portable/carryable order station (shop upgrade, "iPad item") described in the
/// design doc isn't implemented -- there's no shop system yet to unlock it from.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class OrderStation : NetworkBehaviour
{
    [SerializeField] private MaterialItem[] availableMaterials;
    [SerializeField] private int orderQuantity = 3;
    [SerializeField] private Transform deliveryPoint;

    public int MaterialCount => availableMaterials.Length;

    public bool WouldExceedCap =>
        OrderQueueSystem.Instance.LiveMaterialCount.Value
        + OrderQueueSystem.Instance.PendingOrderedQuantity.Value
        + orderQuantity > OrderQueueSystem.Instance.MaterialCap;

    public string DescribeOption(int index) => $"Order {orderQuantity} {availableMaterials[index].Type}";

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void PlaceOrderRpc(int materialIndex)
    {
        if (materialIndex < 0 || materialIndex >= availableMaterials.Length) return;

        var material = availableMaterials[materialIndex];
        OrderQueueSystem.Instance.TryPlaceOrder(material, orderQuantity, ResolveDeliveryPosition(material));
    }

    private Vector3 ResolveDeliveryPosition(MaterialItem material)
    {
        if (deliveryPoint != null) return deliveryPoint.position;

        foreach (var zone in SupplyZoneSpawner.All)
            if (zone.MaterialType == material.Type)
                return zone.transform.position;

        // No matching supply zone in the scene -- fall back to the station itself
        // rather than silently dropping the order.
        return transform.position;
    }
}
