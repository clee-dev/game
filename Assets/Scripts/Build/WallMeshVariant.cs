/// <summary>
/// The 6 mesh shapes a Wall tile's connection bitmask resolves to
/// (Systems Architecture, Section 10 -- Smart Wall System). See WallVariantLookup
/// for the bitmask-to-variant mapping and WallMeshSet for the per-variant meshes.
/// </summary>
public enum WallMeshVariant
{
    Standalone,
    EndCap,
    Straight,
    Corner,
    TJunction,
    Cross,
}
