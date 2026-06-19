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
/// can offer different tool subsets per the blueprint's ToolDepotData.tools.
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
