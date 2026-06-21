using UnityEngine;

/// <summary>
/// Marker for the dedicated attach-point collider a second player interacts with to bind
/// into a TwoPersonCarry. Place on a small child object (e.g. a handle mesh) of the
/// carryable prefab, separate from the main pickup collider, with its own Collider --
/// PlayerInteraction's raycast resolves this the same way it resolves PhysicsPickup,
/// BuildTile, etc. Assign carry to the sibling TwoPersonCarry on the prefab root.
/// </summary>
public class TwoPersonCarryPoint : MonoBehaviour
{
    [SerializeField] private TwoPersonCarry carry;

    public TwoPersonCarry Carry => carry;
}
