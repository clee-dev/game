using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Fixed-position deletion point (player-requested safety valve for ordering mistakes --
/// not in the original design docs). Players raycast-target this via PlayerInteraction and
/// press E while holding a material or tool to permanently despawn it, freeing its slot
/// against OrderQueueSystem's material cap without needing to carry it back to where it
/// came from or throw it somewhere out of the way.
///
/// Needs to be a NetworkObject -- like OrderStation, deleting an item is a client-to-server
/// RPC, and RPCs live on NetworkBehaviours.
///
/// Setup: NetworkObject (registered in the NetworkManager's prefab list), a Collider for
/// PlayerInteraction's raycast to hit, this script.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class TrashBin : NetworkBehaviour
{
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void TrashItemRpc(NetworkObjectReference itemRef)
    {
        if (!itemRef.TryGet(out NetworkObject itemObj)) return;
        if (itemObj.GetComponent<PhysicsPickup>() == null) return;

        itemObj.Despawn();
    }
}
