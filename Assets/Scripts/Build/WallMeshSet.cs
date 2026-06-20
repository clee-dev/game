using UnityEngine;

/// <summary>
/// Designer-tunable mesh set for one wall style -- six meshes, one per
/// WallMeshVariant (Systems Architecture, Section 10 -- Smart Wall System).
/// Assigned to BuildTile.wallMeshSet in the Inspector. Swapping the meshes here
/// (e.g. prototype cubes for real modular wall art) needs zero code changes --
/// WallVariantLookup only ever deals in WallMeshVariant, never a specific mesh.
/// </summary>
[CreateAssetMenu(fileName = "WallMeshSet", menuName = "Crazy Construction/Wall Mesh Set")]
public class WallMeshSet : ScriptableObject
{
    [SerializeField] private Mesh standaloneMesh;
    [SerializeField] private Mesh endCapMesh;
    [SerializeField] private Mesh straightMesh;
    [SerializeField] private Mesh cornerMesh;
    [SerializeField] private Mesh tJunctionMesh;
    [SerializeField] private Mesh crossMesh;

    public Mesh MeshFor(WallMeshVariant variant) => variant switch
    {
        WallMeshVariant.Standalone => standaloneMesh,
        WallMeshVariant.EndCap     => endCapMesh,
        WallMeshVariant.Straight   => straightMesh,
        WallMeshVariant.Corner     => cornerMesh,
        WallMeshVariant.TJunction  => tJunctionMesh,
        WallMeshVariant.Cross      => crossMesh,
        _                          => null,
    };
}
