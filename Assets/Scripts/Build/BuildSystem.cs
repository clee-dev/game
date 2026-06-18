using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Loads the active blueprint and owns the runtime lookup structures every other
/// build-related system queries against (Systems Architecture, Section 2).
///
/// Every machine (host and clients) loads the same blueprint JSON from
/// StreamingAssets independently and builds an identical tilesByPosition lookup --
/// this is plain local computation, not networked. Only the live BuildTile
/// NetworkObjects (and their NetworkVariable state) are server-spawned and synced.
///
/// Setup: create an empty GameObject in the level scene (not a NetworkObject -- it
/// doesn't need to be), add this script, assign tilePrefab (a BuildTile NetworkObject
/// prefab registered in the NetworkManager's prefab list), supplyZoneSpawnerPrefab
/// (a plain, non-networked prefab with SupplyZoneSpawner on it), and
/// toolDepotSpawnerPrefab (same idea, with ToolDepotSpawner). Like supply zones, which
/// tool a depot spawns is configured on the prefab itself, not read from the blueprint's
/// ToolDepotData.tools -- fine for the MVP's single tool (hammer); supporting multiple
/// distinct depot tool types would need a prefab-per-ToolType lookup instead.
/// </summary>
public class BuildSystem : MonoBehaviour
{
    public static BuildSystem Instance { get; private set; }

    /// <summary>
    /// World units per grid cell. Tiles are spaced out by this much so adjacent
    /// BuildTile colliders aren't flush against each other -- flush colliders make
    /// it nearly impossible to raycast-target one tile over its neighbor.
    /// </summary>
    public const float CellSize = 2f;

    [Header("Blueprint")]
    [SerializeField] private string blueprintId = "blueprint_001";

    [Header("Prefabs")]
    [SerializeField] private BuildTile tilePrefab;
    [SerializeField] private SupplyZoneSpawner supplyZoneSpawnerPrefab;
    [SerializeField] private ToolDepotSpawner toolDepotSpawnerPrefab;

    public BlueprintData CurrentBlueprint { get; private set; }

    private readonly Dictionary<Vector3Int, TileData> _tileDataByPosition = new();
    private readonly Dictionary<Vector3Int, BuildTile> _liveTilesByPosition = new();

    private static readonly Vector3Int[] HorizontalNeighbors =
    {
        Vector3Int.left, Vector3Int.right, Vector3Int.forward, Vector3Int.back
    };

    private void Awake()
    {
        Instance = this;
        LoadBlueprintAndBuildLookups();
    }

    private void Start()
    {
        // Only the host/server spawns the actual tile and supply zone NetworkObjects.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            SpawnFromBlueprint();
    }

    // -------------------------------------------------------------------------
    // Loading (runs identically on every machine)
    // -------------------------------------------------------------------------

    private void LoadBlueprintAndBuildLookups()
    {
        CurrentBlueprint = BlueprintLoader.Load(blueprintId);
        if (CurrentBlueprint == null) return;

        foreach (TileData tile in CurrentBlueprint.tiles)
            _tileDataByPosition[tile.position.ToVector3Int()] = tile;
    }

    public TileData GetTileDataAt(Vector3Int pos) =>
        _tileDataByPosition.TryGetValue(pos, out var data) ? data : null;

    // -------------------------------------------------------------------------
    // Spawning (server only)
    // -------------------------------------------------------------------------

    private void SpawnFromBlueprint()
    {
        foreach (TileData tile in CurrentBlueprint.tiles)
        {
            Vector3 worldPos = (Vector3)tile.position.ToVector3Int() * CellSize;
            var instance = Instantiate(tilePrefab, worldPos, Quaternion.identity);
            instance.GetComponent<NetworkObject>().Spawn();
        }

        foreach (SupplyZoneData zone in CurrentBlueprint.supplyZones)
            Instantiate(supplyZoneSpawnerPrefab, zone.worldPosition.ToVector3(), Quaternion.identity);

        foreach (ToolDepotData depot in CurrentBlueprint.toolDepots)
            Instantiate(toolDepotSpawnerPrefab, depot.worldPosition.ToVector3(), Quaternion.identity);
    }

    // -------------------------------------------------------------------------
    // Live tile registry -- populated by each BuildTile as it spawns
    // -------------------------------------------------------------------------

    public void RegisterTile(Vector3Int pos, BuildTile tile) => _liveTilesByPosition[pos] = tile;

    public void UnregisterTile(Vector3Int pos) => _liveTilesByPosition.Remove(pos);

    public BuildTile GetLiveTileAt(Vector3Int pos) =>
        _liveTilesByPosition.TryGetValue(pos, out var tile) ? tile : null;

    // -------------------------------------------------------------------------
    // Structural dependency rules (Systems Architecture, Section 6.3)
    // -------------------------------------------------------------------------

    public bool IsEligible(BuildTile tile)
    {
        switch (tile.Type)
        {
            case TileType.Foundation:
                return true;

            case TileType.Floor:
            case TileType.Wall:
            case TileType.Column:
            case TileType.Furniture:
                return IsBuiltAt(tile.GridPosition + Vector3Int.down);

            case TileType.Window:
            case TileType.Door:
                foreach (var dir in HorizontalNeighbors)
                    if (IsBuiltAt(tile.GridPosition + dir))
                        return true;
                return false;

            default:
                return false;
        }
    }

    private bool IsBuiltAt(Vector3Int pos)
    {
        var tile = GetLiveTileAt(pos);
        return tile != null && tile.State == TileState.Built;
    }

    /// <summary>
    /// Called by a tile when it becomes Built so its 6 neighbors re-evaluate
    /// eligibility and refresh their ghost visuals. Bounded -- no full graph traversal.
    /// </summary>
    public void RefreshNeighborsOf(Vector3Int pos)
    {
        GetLiveTileAt(pos + Vector3Int.up)?.RefreshEligibility();
        GetLiveTileAt(pos + Vector3Int.down)?.RefreshEligibility();
        foreach (var dir in HorizontalNeighbors)
            GetLiveTileAt(pos + dir)?.RefreshEligibility();
    }

    // -------------------------------------------------------------------------
    // Completion tracking (Section 9.4)
    // -------------------------------------------------------------------------

    public int TotalTiles => _liveTilesByPosition.Count;

    public int BuiltTileCount()
    {
        int count = 0;
        foreach (var tile in _liveTilesByPosition.Values)
            if (tile.State == TileState.Built) count++;
        return count;
    }

    public float CompletionPercent =>
        TotalTiles == 0 ? 0f : (float)BuiltTileCount() / TotalTiles;
}
