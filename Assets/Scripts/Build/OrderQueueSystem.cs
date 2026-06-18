using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Global, server-authoritative material ordering system (Systems Architecture,
/// Section 5.3 -- "rolling prediction logistics"). Enforces a shared cap on every
/// Loose/Held/Placed material currently in the world (regardless of whether it came
/// from an order or a SupplyZoneSpawner) and runs the delivery countdown for every
/// pending order so every client's queue UI stays in sync.
///
/// LiveMaterialCount and PendingOrderedQuantity are both counted against the cap --
/// an order reserves its quantity the moment it's placed, not just once delivered,
/// otherwise the cap could be bypassed by placing several orders inside one delivery
/// window and having them all land at once.
///
/// Setup: place exactly one of these directly in the level scene (it needs a
/// NetworkObject -- unlike BuildSystem, its pending-order queue is genuinely shared
/// server state, not something every client can compute independently). OrderStation
/// instances (spawned by BuildSystem per blueprint orderStations entry) call
/// Instance.TryPlaceOrder; nothing else needs a direct reference.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class OrderQueueSystem : NetworkBehaviour
{
    public static OrderQueueSystem Instance { get; private set; }

    [Header("Tuning")]
    [Tooltip("Design doc calls for scaling this by player count; no formula specified yet, so it's a flat value for now.")]
    [SerializeField] private int materialCap = 15;
    [SerializeField] private float deliveryDelay = 15f;

    public int MaterialCap => materialCap;

    public struct OrderEntry : IEquatable<OrderEntry>, INetworkSerializeByMemcpy
    {
        public MaterialType materialType;
        public int quantity;
        public float remainingSeconds;

        public bool Equals(OrderEntry other) =>
            materialType == other.materialType
            && quantity == other.quantity
            && remainingSeconds.Equals(other.remainingSeconds);
    }

    private readonly NetworkList<OrderEntry> _pendingOrders = new();
    public NetworkList<OrderEntry> PendingOrders => _pendingOrders;

    public readonly NetworkVariable<int> LiveMaterialCount = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public readonly NetworkVariable<int> PendingOrderedQuantity = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Server-only -- mirrors _pendingOrders index-for-index, holding the non-networkable
    // bits (prefab reference, delivery position) the countdown needs to spawn materials.
    private struct PendingDelivery
    {
        public MaterialItem prefab;
        public Vector3 position;
    }
    private readonly List<PendingDelivery> _deliveries = new();

    private void Awake() => Instance = this;

    public override void OnDestroy()
    {
        _pendingOrders.Dispose();
        base.OnDestroy();
    }

    // -------------------------------------------------------------------------
    // Material cap registry -- called by MaterialItem on spawn/despawn, any source
    // -------------------------------------------------------------------------

    public void RegisterMaterialSpawned()
    {
        if (IsServer) LiveMaterialCount.Value++;
    }

    public void RegisterMaterialDespawned()
    {
        if (IsServer) LiveMaterialCount.Value = Mathf.Max(0, LiveMaterialCount.Value - 1);
    }

    // -------------------------------------------------------------------------
    // Placing orders (server-only; OrderStation predicts WouldExceedCap client-side
    // for UI, but the server re-checks here regardless)
    // -------------------------------------------------------------------------

    public bool TryPlaceOrder(MaterialItem materialPrefab, int quantity, Vector3 deliveryPosition)
    {
        if (!IsServer) return false;
        if (LiveMaterialCount.Value + PendingOrderedQuantity.Value + quantity > materialCap) return false;

        PendingOrderedQuantity.Value += quantity;
        _pendingOrders.Add(new OrderEntry
        {
            materialType = materialPrefab.Type,
            quantity = quantity,
            remainingSeconds = deliveryDelay,
        });
        _deliveries.Add(new PendingDelivery { prefab = materialPrefab, position = deliveryPosition });
        return true;
    }

    // -------------------------------------------------------------------------
    // Delivery countdown (server-only)
    // -------------------------------------------------------------------------

    private void Update()
    {
        if (!IsServer) return;

        for (int i = _pendingOrders.Count - 1; i >= 0; i--)
        {
            OrderEntry order = _pendingOrders[i];
            order.remainingSeconds -= Time.deltaTime;

            if (order.remainingSeconds <= 0f)
            {
                Deliver(_deliveries[i], order.quantity);
                PendingOrderedQuantity.Value -= order.quantity;
                _pendingOrders.RemoveAt(i);
                _deliveries.RemoveAt(i);
            }
            else
            {
                _pendingOrders[i] = order;
            }
        }
    }

    private void Deliver(PendingDelivery delivery, int quantity)
    {
        for (int i = 0; i < quantity; i++)
        {
            Vector3 offset = new Vector3(i * 0.5f, 0f, 0f);
            var instance = Instantiate(delivery.prefab, delivery.position + offset, Quaternion.identity);
            instance.GetComponent<NetworkObject>().Spawn();
        }
    }
}
