using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Structural compatibility rules shared by BuildSystem (runtime build eligibility) and
/// the Level Editor (author-time placement validation), so the two can never drift apart.
/// Unlike a generic "needs something below/adjacent" check, each tile type only accepts
/// specific neighbor types -- e.g. a Wall may sit on a Floor but not directly on a
/// Foundation, and only Decor may attach to a Wall. Callers supply what "present" means
/// at a given position: BuildSystem checks for TileState.Built, the Level Editor checks
/// for tile existence in the in-progress blueprint.
/// </summary>
public static class TileStructuralRules
{
    public static readonly Vector3Int[] HorizontalNeighbors =
    {
        Vector3Int.left, Vector3Int.right, Vector3Int.forward, Vector3Int.back
    };

    /// <summary>Which types may be placed directly on top of (supported from below by) a
    /// given type. Foundation is intentionally absent as a value anywhere -- nothing
    /// stacks a Foundation on top of another tile.</summary>
    private static readonly Dictionary<TileType, TileType[]> SupportsAbove = new()
    {
        { TileType.Foundation, new[] { TileType.Floor, TileType.Column } },
        { TileType.Floor,      new[] { TileType.Wall, TileType.Window, TileType.Door, TileType.Furniture, TileType.Column } },
    };

    /// <summary>Which types may attach to (be horizontally adjacent to) a given type.</summary>
    private static readonly Dictionary<TileType, TileType[]> SupportsAdjacent = new()
    {
        { TileType.Wall, new[] { TileType.Decor } },
    };

    public static bool HasSupport(TileType type, Vector3Int pos, Func<Vector3Int, TileType?> typeAt)
    {
        if (type == TileType.Foundation)
            return typeAt(pos + Vector3Int.down) == null; // base of a stack -- must rest on empty space

        TileType? below = typeAt(pos + Vector3Int.down);
        if (below.HasValue && Allows(SupportsAbove, below.Value, type))
            return true;

        foreach (Vector3Int dir in HorizontalNeighbors)
        {
            TileType? neighbor = typeAt(pos + dir);
            if (neighbor.HasValue && Allows(SupportsAdjacent, neighbor.Value, type))
                return true;
        }

        return false;
    }

    /// <summary>Types that would accept `type` directly above them. Used to build
    /// human-readable rejection messages.</summary>
    public static IEnumerable<TileType> SupportsBelowFor(TileType type) =>
        SupportsAbove.Where(kv => kv.Value.Contains(type)).Select(kv => kv.Key);

    /// <summary>Types that would accept `type` as a horizontal neighbor. Used to build
    /// human-readable rejection messages.</summary>
    public static IEnumerable<TileType> SupportsAdjacentFor(TileType type) =>
        SupportsAdjacent.Where(kv => kv.Value.Contains(type)).Select(kv => kv.Key);

    private static bool Allows(Dictionary<TileType, TileType[]> table, TileType support, TileType candidate) =>
        table.TryGetValue(support, out TileType[] allowed) && Array.IndexOf(allowed, candidate) >= 0;
}
