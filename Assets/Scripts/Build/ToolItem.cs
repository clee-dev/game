using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Sits alongside PhysicsPickup on every tool prefab (e.g. the hammer). Tools don't
/// need their own state machine -- PhysicsPickup's held flag is sufficient -- this
/// just tags which tool type it is so BuildTile can validate build interactions.
///
/// Setup for the prefab: Rigidbody, Collider, NetworkObject, ClientNetworkTransform,
/// PhysicsPickup, this script. Set toolType in the Inspector.
/// </summary>
[RequireComponent(typeof(PhysicsPickup), typeof(NetworkObject))]
public class ToolItem : NetworkBehaviour
{
    [SerializeField] private ToolType toolType = ToolType.Hammer;

    public ToolType Type => toolType;
}
