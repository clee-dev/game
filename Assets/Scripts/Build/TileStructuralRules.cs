using System;
using UnityEngine;

/// <summary>
/// Structural dependency rule shared by BuildSystem (runtime build eligibility) and the
/// Level Editor (author-time placement validation), so the two can never drift apart:
/// Foundation has no dependency; Floor/Wall/Column/Furniture need support from the tile
/// below; Window/Door/Decor need support from a horizontal neighbor. Callers supply what
/// "supported" means -- BuildSystem checks for TileState.Built, the Level Editor checks
/// for tile existence in the in-progress blueprint.
/// </summary>
public static class TileStructuralRules
{
    public static readonly Vector3Int[] HorizontalNeighbors =
    {
        Vector3Int.left, Vector3Int.right, Vector3Int.forward, Vector3Int.back
    };

    public static bool HasSupport(TileType type, Vector3Int pos, Func<Vector3Int, bool> isSupportedAt)
    {
        switch (type)
        {
            case TileType.Foundation:
                return true;

            case TileType.Floor:
            case TileType.Wall:
            case TileType.Column:
            case TileType.Furniture:
                return isSupportedAt(pos + Vector3Int.down);

            case TileType.Window:
            case TileType.Door:
            case TileType.Decor:
                foreach (Vector3Int dir in HorizontalNeighbors)
                    if (isSupportedAt(pos + dir))
                        return true;
                return false;

            default:
                return false;
        }
    }
}
