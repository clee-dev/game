using UnityEngine;

/// <summary>
/// Marker for one of a TwoPersonCarry item's two interchangeable attach-point colliders --
/// either player can grab either point (TwoPersonCarry resolves what that means: first grab
/// = solo carry, second grab by someone else = shared). Place on a small child object (e.g.
/// a handle mesh) of the carryable prefab, separate from the main pickup collider, with its
/// own Collider -- PlayerInteraction's raycast resolves this the same way it resolves
/// PhysicsPickup, BuildTile, etc. Assign carry to the sibling TwoPersonCarry on the prefab
/// root, and give each of the two points a distinct pointIndex (0 and 1).
/// </summary>
public class TwoPersonCarryPoint : MonoBehaviour
{
    [SerializeField] private TwoPersonCarry carry;
    [SerializeField] private int pointIndex;

    public TwoPersonCarry Carry => carry;
    public int PointIndex => pointIndex;
}
