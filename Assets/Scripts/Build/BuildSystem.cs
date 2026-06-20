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
/// (a plain, non-networked prefab with SupplyZoneSpawner on it), toolDepotSpawnerPrefab
/// (same idea, with ToolDepotSpawner), and orderStationPrefab (an OrderStation
/// NetworkObject prefab, registered in the prefab list like tilePrefab -- unlike the
/// spawners, OrderStation is itself a NetworkObject since players need to RPC it
/// directly). Which material an order station orders is configured on the prefab
/// itself (fine for the MVP's single material). Tool depots are the exception --
/// toolDepotSpawnerPrefab needs every ToolItem type assigned in its toolPrefabs array,
/// and SpawnFromBlueprint() calls Configure(depot.tools) on each instance so depots
/// can offer different tool subsets per the blueprint's ToolDepotData.tools. trashBinPrefab
/// is the same shape as orderStationPrefab (also a NetworkObject, since deleting a held
/// item is a client-to-server RPC); its blueprint array is null-guarded since trashBins
/// is a newer schema field that older saved blueprints won't have.
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
    [SerializeField] private OrderStation orderStationPrefab;
    [SerializeField] private TrashBin trashBinPrefab;

    public BlueprintData CurrentBlueprint { get; private set; }

    private readonly Dictionary<Vector3Int, TileData> _tileDataByPosition = new();
    private readonly Dictionary<Vector3Int, BuildTile> _liveTilesByPosition = new();

    private int _nextPlayerSpawnIndex;

    /// <summary>Offsets for players beyond the blueprint's defined spawn points -- one of
    /// these is added (at random) to a cycled-through defined spawn so extras don't stack
    /// on top of an existing player.</summary>
    private static readonly Vector3[] OverflowSpawnOffsets =
    {
        new(3f, 0f, 0f), new(-3f, 0f, 0f),
        new(0f, 0f, 3f), new(0f, 0f, -3f),
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
        string idToLoad = !string.IsNullOrEmpty(GameSession.Instance?.SelectedBlueprintId)
            ? GameSession.Instance.SelectedBlueprintId
            : blueprintId;

        CurrentBlueprint = BlueprintLoader.Load(idToLoad);
        if (CurrentBlueprint == null) return;

        foreach (TileData tile in CurrentBlueprint.tiles)
            _tileDataByPosition[tile.position.ToVector3Int()] = tile;
    }

    public TileData GetTileDataAt(Vector3Int pos) =>
        _tileDataByPosition.TryGetValue(pos, out var data) ? data : null;

    /// <summary>Server-only. Hands out the blueprint's playerSpawns in order, one per call
    /// (NetworkPlayer calls this once per player as they arrive in Game1). Once every
    /// defined spawn point has been handed out once, further calls cycle back through them
    /// with a random 3-unit cardinal offset so extra players don't stack on an existing one.
    /// _nextPlayerSpawnIndex resets naturally every time Game1 is (re-)loaded, since this
    /// MonoBehaviour and its Awake() run fresh on every scene load.</summary>
    public Vector3 GetPlayerSpawnPosition()
    {
        WorldPosition[] spawns = CurrentBlueprint?.playerSpawns;
        if (spawns == null || spawns.Length == 0) return Vector3.zero;

        int idx = _nextPlayerSpawnIndex++;
        Vector3 basePos = spawns[idx % spawns.Length].ToVector3();
        if (idx < spawns.Length) return basePos;

        return basePos + OverflowSpawnOffsets[Random.Range(0, OverflowSpawnOffsets.Length)];
    }

    // -------------------------------------------------------------------------
    // Spawning (server only)
    // -------------------------------------------------------------------------

    private void SpawnFromBlueprint()
    {
        foreach (TileData tile in CurrentBlueprint.tiles)
        {
            Vector3 worldPos = (Vector3)tile.position.ToVector3Int() * CellSize;
            var instance = Instantiate(tilePrefab, worldPos, Quaternion.identity);
            var netObj = instance.GetComponent<NetworkObject>();
            netObj.Spawn();
            netObj.DestroyWithScene = true;
        }

        // Second pass (Smart Walls, Section 10): every tile is registered in
        // _liveTilesByPosition by now (BuildTile.OnNetworkSpawn runs synchronously from
        // Spawn() above), so each Wall tile can see its neighbors to compute its initial
        // connection mask. Doing this inside the loop above would miss not-yet-spawned
        // neighbors further along in tiles.
        foreach (BuildTile liveTile in _liveTilesByPosition.Values)
            if (liveTile.Type == TileType.Wall)
                liveTile.RecalculateWallMask();

        foreach (SupplyZoneData zone in CurrentBlueprint.supplyZones)
            Instantiate(supplyZoneSpawnerPrefab, zone.worldPosition.ToVector3(), Quaternion.identity);

        foreach (ToolDepotData depot in CurrentBlueprint.toolDepots)
        {
            var instance = Instantiate(toolDepotSpawnerPrefab, depot.worldPosition.ToVector3(), Quaternion.identity);
            instance.Configure(depot.tools);
        }

        foreach (OrderStationData station in CurrentBlueprint.orderStations)
        {
            var instance = Instantiate(orderStationPrefab, station.worldPosition.ToVector3(), Quaternion.identity);
            var netObj = instance.GetComponent<NetworkObject>();
            netObj.Spawn();
            netObj.DestroyWithScene = true;
        }

        // Null-guarded unlike the loops above -- trashBins is a newer schema field absent
        // from blueprints saved before it existed, so a missing array must not throw.
        foreach (TrashBinData bin in CurrentBlueprint.trashBins ?? System.Array.Empty<TrashBinData>())
        {
            var instance = Instantiate(trashBinPrefab, bin.worldPosition.ToVector3(), Quaternion.identity);
            var netObj = instance.GetComponent<NetworkObject>();
            netObj.Spawn();
            netObj.DestroyWithScene = true;
        }
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

    public bool IsEligible(BuildTile tile) =>
        TileStructuralRules.HasSupport(tile.Type, tile.GridPosition, GetBuiltTypeAt);

    private TileType? GetBuiltTypeAt(Vector3Int pos)
    {
        var tile = GetLiveTileAt(pos);
        return tile != null && tile.State == TileState.Built ? tile.Type : (TileType?)null;
    }

    /// <summary>
    /// Called by a tile when it becomes Built so its 6 neighbors re-evaluate
    /// eligibility and refresh their ghost visuals. Bounded -- no full graph traversal.
    /// </summary>
    public void RefreshNeighborsOf(Vector3Int pos)
    {
        GetLiveTileAt(pos + Vector3Int.up)?.RefreshEligibility();
        GetLiveTileAt(pos + Vector3Int.down)?.RefreshEligibility();
        foreach (var dir in TileStructuralRules.HorizontalNeighbors)
            GetLiveTileAt(pos + dir)?.RefreshEligibility();
    }

    /// <summary>
    /// Server-only, one-hop (Section 10 -- Smart Wall System). Called by a Wall tile
    /// whenever its own state changes, so its Wall neighbors re-run their bitmask and pick
    /// up/drop the connection. Wall connections are Wall-to-Wall only -- Door/Window are
    /// out of scope (see SMART_WALLS_1.md open questions) -- so non-Wall neighbors are
    /// skipped rather than recalculated.
    /// </summary>
    public void NotifyNeighborsForWallMask(Vector3Int origin)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        foreach (var dir in TileStructuralRules.HorizontalNeighbors)
        {
            BuildTile neighbor = GetLiveTileAt(origin + dir);
            if (neighbor != null && neighbor.Type == TileType.Wall)
                neighbor.RecalculateWallMask();
        }
    }

    // -------------------------------------------------------------------------
    // Structural collapse cascade (Section 4.4 -- "Jenga-style collapse"). Server-only,
    // called from BuildTile.OnStateChanged when a tile becomes Destroyed.
    //
    // Deliberately reuses TileStructuralRules.HasSupport (via IsEligible) instead of
    // maintaining a separate precomputed supportDependents graph: HasSupport already
    // re-derives "is this position currently supported" live from neighbor tile
    // states, so a cached reverse-edge graph would just be a second copy of the same
    // information with its own staleness risk. Only the up and 4 horizontal neighbors
    // are checked -- those are the only positions TileStructuralRules lets anything
    // depend on (support never flows downward, so "down" is never re-checked here).
    // Each collapse re-enters this method through OnStateChanged, so the cascade
    // continues automatically without explicit recursion bookkeeping.
    // -------------------------------------------------------------------------

    public void CascadeCollapseFrom(Vector3Int pos)
    {
        CollapseIfUnsupported(pos + Vector3Int.up);
        foreach (var dir in TileStructuralRules.HorizontalNeighbors)
            CollapseIfUnsupported(pos + dir);
    }

    private void CollapseIfUnsupported(Vector3Int pos)
    {
        BuildTile tile = GetLiveTileAt(pos);
        if (tile == null) return;
        if (tile.State != TileState.MaterialPlaced && tile.State != TileState.Built) return;
        if (IsEligible(tile)) return; // still supported by something else

        tile.Collapse();
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

    // -------------------------------------------------------------------------
    // Level end (Phase B Timer System / Win-Loss Conditions, PLANNED_FEATURES.md)
    // -------------------------------------------------------------------------

    public bool LevelEnded { get; private set; }

    /// <summary>Decides success/failure once the level is over -- either naturally
    /// (every tile Built, forced == false, called from BuildTile.OnStateChanged) or
    /// forced by LevelTimer hitting zero (forced == true, evaluated at whatever
    /// completion percentage was reached). Runs identically on every machine since both
    /// triggers derive from already-replicated NetworkVariable state -- no IsServer
    /// gating needed here. Payout calculation and the post-level scene transition are not
    /// implemented yet (Win/Loss Conditions, still open in PLANNED_FEATURES.md); this just
    /// fires the shared success/fail signal those will eventually consume.</summary>
    public void EvaluateCompletion(bool forced = false)
    {
        if (LevelEnded) return;
        if (!forced && BuiltTileCount() < TotalTiles) return;

        LevelEnded = true;
        float fullThreshold = CurrentBlueprint?.contractDefaults?.completionThresholds?.full ?? 1f;
        GameEvents.FireLevelEnded(CompletionPercent >= fullThreshold, CompletionPercent);
    }
}
