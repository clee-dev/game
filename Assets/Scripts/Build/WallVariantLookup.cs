/// <summary>
/// Bitmask autotiling table for Wall tiles (Systems Architecture, Section 10 --
/// Smart Wall System). The mask is 4 bits, one per horizontal cardinal direction,
/// set when that neighbor is itself a Wall tile that's MaterialPlaced or Built.
/// Both BuildTile (runtime) and LevelEditorController (author-time) build masks
/// with these same bit values so they resolve to identical variants/rotations.
///
/// Rotation convention: EndCap's open end faces South (-Z) at 0 deg; Straight runs
/// N-S at 0 deg; Corner connects North+East at 0 deg; TJunction's open side faces
/// West at 0 deg; all rotate clockwise as the mask changes. Cross and Standalone
/// are rotationally symmetric (always 0 deg).
/// </summary>
public static class WallVariantLookup
{
    public const byte North = 1;
    public const byte East = 2;
    public const byte South = 4;
    public const byte West = 8;

    public static (WallMeshVariant variant, float yRotation) GetVariant(byte mask) => mask switch
    {
        0  => (WallMeshVariant.Standalone, 0f),
        1  => (WallMeshVariant.EndCap, 180f),
        2  => (WallMeshVariant.EndCap, 270f),
        3  => (WallMeshVariant.Corner, 0f),
        4  => (WallMeshVariant.EndCap, 0f),
        5  => (WallMeshVariant.Straight, 0f),
        6  => (WallMeshVariant.Corner, 90f),
        7  => (WallMeshVariant.TJunction, 90f),
        8  => (WallMeshVariant.EndCap, 90f),
        9  => (WallMeshVariant.Corner, 270f),
        10 => (WallMeshVariant.Straight, 90f),
        11 => (WallMeshVariant.TJunction, 0f),
        12 => (WallMeshVariant.Corner, 180f),
        13 => (WallMeshVariant.TJunction, 270f),
        14 => (WallMeshVariant.TJunction, 180f),
        15 => (WallMeshVariant.Cross, 0f),
        _  => (WallMeshVariant.Standalone, 0f),
    };
}
