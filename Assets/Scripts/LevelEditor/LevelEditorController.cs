using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Top-level orchestrator for the Level Editor (Systems Architecture, Section 11): owns
/// the in-memory EditableBlueprint, the undo/redo command stack, the current mode/Y-layer/
/// brush, and the placeholder visuals (primitive cubes/spheres -- no art needed). All
/// mutations to Blueprint go through Commands.Run() so they're undoable.
///
/// Click-to-grid-cell targeting is plain math against a horizontal plane
/// (LevelEditorCamera.ScreenToPlane), not physics raycasting against the tile visuals --
/// there's no need for the visuals to carry colliders for editing input. Tile cubes keep
/// their default BoxCollider anyway, since Preview Mode's CharacterController needs
/// something solid to stand on/bump into. The editor camera is a perfectly vertical
/// orthographic top-down view, so a click's resolved (x, z) is the same regardless of
/// which Y the targeting plane sits at -- only LayerMode.Manual's tile placement and
/// LayerMode.Auto's column scan ever care about Y.
///
/// Layer handling has two modes (LayerMode below). Auto (the default) stacks tile
/// placement/erase on whatever's already in that grid column -- a Floor placed above an
/// existing Foundation just lands on top of it, no manual layer switch needed. Manual
/// keeps the original behavior (CurrentLayer fixed until changed via SetLayer/the bracket
/// keys/the UI buttons) for designers who want exact control over which layer a click
/// lands on, e.g. to select an existing tile for property editing -- Auto mode's clicks
/// always target the next empty slot in the column, so it can't select an already-placed
/// tile. World Objects (spawners/kiosks/depots/etc.) are never tied to either mode or to
/// CurrentLayer at all -- they always place at WorldObjectHeight, the ground/walking-
/// surface level, so they can never end up floating at whatever Y a tile brush happened
/// to leave CurrentLayer at.
///
/// Setup: create an empty GameObject in a new "LevelEditor" scene (do not name any folder
/// "Editor" -- Unity strips those from player builds), add this script, an
/// EditorGridRenderer (pointed back at this controller), and a LevelEditorUI. Add a Camera
/// child/sibling GameObject with a LevelEditorCamera component and assign it to
/// editorCamera below.
/// </summary>
public class LevelEditorController : MonoBehaviour
{
    public enum EditorMode { Tiles, WorldObjects, Preview }
    public enum WorldObjectCategory { SupplyZone, OrderStation, ToolDepot, TrashBin, PlayerSpawn }

    /// <summary>Auto stacks tile placement/erase on whatever's already in the clicked
    /// column (no manual layer switching). Manual keeps CurrentLayer fixed until changed
    /// explicitly -- needed to select an existing tile for property editing, which Auto's
    /// always-target-the-next-empty-slot behavior can't do. See the class doc above.</summary>
    public enum LayerMode { Auto, Manual }

    public const float CellSize = BuildSystem.CellSize;

    /// <summary>Fixed world Y every World Object (SupplyZone/OrderStation/ToolDepot/
    /// TrashBin/PlayerSpawn) places at, regardless of LayerMode or CurrentLayer -- the
    /// ground/walking-surface level. Decoupling this from CurrentLayer is what stops a
    /// spawner or kiosk from floating at whatever Y a tile brush left CurrentLayer at.</summary>
    public const float WorldObjectHeight = 1f;

    /// <summary>Set in Awake -- LevelEditorBlueprintSync reaches through this to read/replace
    /// Blueprint on the one LevelEditorController instance that exists per client.</summary>
    public static LevelEditorController Instance { get; private set; }

    [SerializeField] private LevelEditorCamera editorCamera;

    /// <summary>Pause overlay Canvas (PauseMenu) -- hidden during Preview Mode so its
    /// unconditional Escape handling doesn't fight LevelEditorPreviewController's own
    /// Escape-to-exit-Preview binding.</summary>
    [SerializeField] private GameObject pauseCanvas;

    /// <summary>Same asset BuildTile.wallMeshSet reads at runtime (Smart Wall System,
    /// Section 10) -- lets Wall tile previews show the real connection-shaped mesh
    /// instead of an undifferentiated cube. Null-safe: CreateTileCube falls back to the
    /// plain colored cube (every other TileType's look) when this is unassigned.</summary>
    [SerializeField] private WallMeshSet wallMeshSet;

    /// <summary>Host-only editing (per Cameron: "only the host can make edits, other
    /// players just get the camera"). The host/server in this P2P transport IS the
    /// editing player, so this maps directly onto NGO's IsServer.</summary>
    public bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    public EditableBlueprint Blueprint { get; private set; } = new();
    public EditorCommandStack Commands { get; } = new();

    /// <summary>Fired whenever Blueprint changes (an undoable edit, or a wholesale
    /// New/Load) -- LevelEditorBlueprintSync rebroadcasts to non-host clients on this.</summary>
    public event Action BlueprintChanged;

    public EditorMode Mode { get; private set; } = EditorMode.Tiles;
    public LayerMode CurrentLayerMode { get; private set; } = LayerMode.Auto;
    public int CurrentLayer { get; private set; }
    public Vector3Int? SelectedTilePos { get; private set; }

    /// <summary>Set whenever a placement/type-change is rejected by the structural
    /// dependency rule below; LevelEditorUI surfaces it to the designer.</summary>
    public string PlacementWarning { get; private set; } = "";

    // Brush state -- read/written directly by LevelEditorUI.
    public TileType BrushTileType = TileType.Foundation;
    public MaterialType BrushMaterial = MaterialType.Wood;
    public ToolType BrushTool = ToolType.Hammer;
    public int BrushHealth = 100;
    public WorldObjectCategory BrushCategory = WorldObjectCategory.SupplyZone;

    private Transform _visualsRoot;
    private readonly Dictionary<Vector3Int, GameObject> _tileVisuals = new();
    private readonly List<GameObject> _worldObjectVisuals = new();
    private readonly Dictionary<Color, Material> _materialCache = new();

    /// <summary>Wall connection variant + Y rotation per Wall tile position (Smart Wall
    /// System, Section 10), author-time mirror of BuildTile's runtime _wallMask --
    /// existence-based instead of state-based, same as CanPlaceTileType's structural
    /// check, since the editor's blueprint has no concept of MaterialPlaced/Built.
    /// Rebuilt in full on every blueprint change rather than incrementally -- the
    /// blueprint is small enough that a full pass is fine, and it's the only option for
    /// undo/redo, which don't expose which position they touched.</summary>
    private readonly Dictionary<Vector3Int, (WallMeshVariant variant, float yRotation)> _editorWallVariants = new();

    private LevelEditorPreviewController _activePreview;

    private void Awake()
    {
        Instance = this;
        Commands.Changed += () => BlueprintChanged?.Invoke();
        BlueprintChanged += () => { RebuildEditorWallVariants(); RefreshAllTileVisuals(); };

        _visualsRoot = new GameObject("EditorVisuals").transform;
        _visualsRoot.SetParent(transform);
        RebuildEditorWallVariants();
        RefreshAllTileVisuals();
        RefreshWorldObjectVisuals();
    }

    private void Update()
    {
        // Non-host clients are spectators: free camera (LevelEditorCamera is its own
        // per-client local MonoBehaviour, unaffected by this), no editing input.
        if (!IsHost) return;
        if (Mode == EditorMode.Preview) return;

        HandleLayerKeys();
        HandleUndoRedoKeys();
        HandleClickInput();
    }

    // -------------------------------------------------------------------------
    // Mode / layer
    // -------------------------------------------------------------------------

    public void SetMode(EditorMode mode)
    {
        if (mode == EditorMode.Preview) EnterPreviewMode();
        else if (Mode == EditorMode.Preview) ExitPreviewMode();
        else Mode = mode;
    }

    public void SetLayer(int layer)
    {
        CurrentLayer = Mathf.Clamp(layer, 0, Blueprint.GridSize.y - 1);
        RefreshAllTileVisuals();
    }

    /// <summary>Switches between Auto (stack on/unstack from the clicked column) and
    /// Manual (CurrentLayer fixed until changed explicitly) tile-layer targeting. See the
    /// LayerMode enum doc above.</summary>
    public void SetLayerMode(LayerMode mode) => CurrentLayerMode = mode;

    private void HandleLayerKeys()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.leftBracketKey.wasPressedThisFrame) SetLayer(CurrentLayer - 1);
        if (kb.rightBracketKey.wasPressedThisFrame) SetLayer(CurrentLayer + 1);
    }

    private void HandleUndoRedoKeys()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
        if (!ctrl) return;

        if (kb.zKey.wasPressedThisFrame) Commands.Undo();
        if (kb.yKey.wasPressedThisFrame) Commands.Redo();
    }

    // -------------------------------------------------------------------------
    // Click input -- routes to tile or world-object placement/erase depending on mode.
    // -------------------------------------------------------------------------

    private void HandleClickInput()
    {
        var mouse = Mouse.current;
        if (mouse == null || LevelEditorUI.IsPointerOverUI) return;

        if (mouse.leftButton.wasPressedThisFrame) HandleClick(erase: false);
        if (mouse.rightButton.wasPressedThisFrame) HandleClick(erase: true);
    }

    private void HandleClick(bool erase)
    {
        // The editor camera is a perfectly vertical orthographic top-down view, so the
        // plane's Y has no effect on the resolved (x, z) -- Y=0 is just a convenient
        // constant. Tile mode resolves its own Y below (Auto: column scan, Manual:
        // CurrentLayer); World Objects always use the fixed WorldObjectHeight.
        Vector3 groundHit = editorCamera.ScreenToPlane(Mouse.current.position.ReadValue(), 0f);

        if (Mode == EditorMode.Tiles)
        {
            HandleTileClick(groundHit, erase);
        }
        else if (Mode == EditorMode.WorldObjects)
        {
            Vector3 worldPos = new Vector3(groundHit.x, WorldObjectHeight, groundHit.z);
            if (erase) EraseNearestWorldObject(worldPos);
            else PlaceWorldObject(worldPos);
        }
    }

    private void HandleTileClick(Vector3 groundHit, bool erase)
    {
        int gx = Mathf.FloorToInt(groundHit.x / CellSize);
        int gz = Mathf.FloorToInt(groundHit.z / CellSize);
        if (gx < 0 || gx >= Blueprint.GridSize.x || gz < 0 || gz >= Blueprint.GridSize.z) return;

        if (CurrentLayerMode == LayerMode.Manual)
        {
            var pos = new Vector3Int(gx, CurrentLayer, gz);
            if (erase) EraseTile(pos);
            else PlaceOrSelectTile(pos);
            return;
        }

        // Auto mode -- target whatever's already in this column instead of CurrentLayer,
        // so building upward (or tearing down) never requires a manual layer switch.
        if (erase)
        {
            int topY = FindTopmostOccupiedY(gx, gz);
            if (topY < 0) return; // nothing in this column

            EraseTile(new Vector3Int(gx, topY, gz));
            CurrentLayer = topY;
        }
        else
        {
            int y = FindLowestEmptyY(gx, gz);
            if (y < 0)
            {
                PlacementWarning = "Column is full -- increase Grid Size Y to build higher.";
                return;
            }

            PlaceOrSelectTile(new Vector3Int(gx, y, gz));
            CurrentLayer = y;
        }
    }

    private int FindLowestEmptyY(int gx, int gz)
    {
        for (int y = 0; y < Blueprint.GridSize.y; y++)
            if (!Blueprint.Tiles.ContainsKey(new Vector3Int(gx, y, gz))) return y;
        return -1;
    }

    private int FindTopmostOccupiedY(int gx, int gz)
    {
        for (int y = Blueprint.GridSize.y - 1; y >= 0; y--)
            if (Blueprint.Tiles.ContainsKey(new Vector3Int(gx, y, gz))) return y;
        return -1;
    }

    // -------------------------------------------------------------------------
    // Tile placement / erase / property edit -- all undoable.
    // -------------------------------------------------------------------------

    private void PlaceOrSelectTile(Vector3Int pos)
    {
        if (Blueprint.Tiles.ContainsKey(pos))
        {
            SelectedTilePos = pos;
            return;
        }

        if (!CanPlaceTileType(BrushTileType, pos))
        {
            PlacementWarning = BuildPlacementWarning(BrushTileType);
            return;
        }

        var newTile = new TileData
        {
            id = Blueprint.NextTileId(),
            position = new GridPosition { x = pos.x, y = pos.y, z = pos.z },
            type = BrushTileType.ToString().ToLowerInvariant(),
            requiredMaterial = BrushMaterial.ToString().ToLowerInvariant(),
            requiredTool = BrushTool.ToString().ToLowerInvariant(),
            health = BrushHealth,
        };

        Commands.Run(new ActionCommand(
            execute: () => { Blueprint.Tiles[pos] = newTile; RefreshTileVisual(pos); },
            undo: () => { Blueprint.Tiles.Remove(pos); RefreshTileVisual(pos); }));

        SelectedTilePos = pos;
        PlacementWarning = "";
    }

    // -------------------------------------------------------------------------
    // Structural dependency rule (Systems Architecture, Section 6.3) -- the same rule
    // BuildSystem uses to decide runtime build eligibility, applied here against tile
    // *existence* in the blueprint rather than TileState.Built, so the editor can't
    // author a layout that would never be buildable (e.g. a Wall with no Floor below,
    // or Furniture floating with nothing under it).
    // -------------------------------------------------------------------------

    public bool CanPlaceTileType(TileType type, Vector3Int pos) =>
        TileStructuralRules.HasSupport(type, pos, GetBlueprintTypeAt);

    private TileType? GetBlueprintTypeAt(Vector3Int pos) =>
        Blueprint.Tiles.TryGetValue(pos, out TileData t) ? BlueprintEnums.ParseTileType(t.type) : (TileType?)null;

    private static string BuildPlacementWarning(TileType type)
    {
        if (type == TileType.Foundation)
            return "Cannot place Foundation here -- it must rest on empty ground, not on top of another tile.";

        var below = TileStructuralRules.SupportsBelowFor(type).ToArray();
        var adjacent = TileStructuralRules.SupportsAdjacentFor(type).ToArray();

        if (below.Length > 0 && adjacent.Length > 0)
            return $"Cannot place {type} here -- needs to be on top of {string.Join(" or ", below)}, or adjacent to {string.Join(" or ", adjacent)}.";
        if (below.Length > 0)
            return $"Cannot place {type} here -- needs to be on top of {string.Join(" or ", below)}.";
        if (adjacent.Length > 0)
            return $"Cannot place {type} here -- needs to be adjacent to {string.Join(" or ", adjacent)}.";

        return $"Cannot place {type} here.";
    }

    private void EraseTile(Vector3Int pos)
    {
        if (!Blueprint.Tiles.TryGetValue(pos, out TileData existing)) return;

        Commands.Run(new ActionCommand(
            execute: () => { Blueprint.Tiles.Remove(pos); RefreshTileVisual(pos); },
            undo: () => { Blueprint.Tiles[pos] = existing; RefreshTileVisual(pos); }));

        if (SelectedTilePos == pos) SelectedTilePos = null;
    }

    public TileData GetSelectedTile() =>
        SelectedTilePos.HasValue && Blueprint.Tiles.TryGetValue(SelectedTilePos.Value, out TileData t) ? t : null;

    public void DeselectTile() => SelectedTilePos = null;

    public void SetSelectedTileType(TileType type)
    {
        if (SelectedTilePos.HasValue && !CanPlaceTileType(type, SelectedTilePos.Value))
        {
            PlacementWarning = BuildPlacementWarning(type);
            return;
        }

        PlacementWarning = "";
        MutateSelectedTile(t => t.type = type.ToString().ToLowerInvariant());
    }

    public void SetSelectedTileMaterial(MaterialType material) => MutateSelectedTile(t => t.requiredMaterial = material.ToString().ToLowerInvariant());
    public void SetSelectedTileTool(ToolType tool) => MutateSelectedTile(t => t.requiredTool = tool.ToString().ToLowerInvariant());
    public void SetSelectedTileHealth(int health) => MutateSelectedTile(t => t.health = Mathf.Max(1, health));

    private void MutateSelectedTile(Action<TileData> apply)
    {
        if (SelectedTilePos == null) return;
        Vector3Int pos = SelectedTilePos.Value;

        TileData oldCopy = CloneTile(Blueprint.Tiles[pos]);
        TileData newCopy = CloneTile(Blueprint.Tiles[pos]);
        apply(newCopy);

        Commands.Run(new ActionCommand(
            execute: () => { Blueprint.Tiles[pos] = newCopy; RefreshTileVisual(pos); },
            undo: () => { Blueprint.Tiles[pos] = oldCopy; RefreshTileVisual(pos); }));
    }

    private static TileData CloneTile(TileData t) => new TileData
    {
        id = t.id,
        position = t.position,
        type = t.type,
        requiredMaterial = t.requiredMaterial,
        requiredTool = t.requiredTool,
        health = t.health,
    };

    // -------------------------------------------------------------------------
    // World object placement / erase -- supply zones, order stations, tool depots,
    // player spawns. Free-floating world positions, not snapped to the tile grid.
    // -------------------------------------------------------------------------

    private void PlaceWorldObject(Vector3 worldPos)
    {
        switch (BrushCategory)
        {
            case WorldObjectCategory.SupplyZone:
                PlaceOrReplace(Blueprint.SupplyZones, d => d.worldPosition.ToVector3(), worldPos,
                    () => new SupplyZoneData { id = Blueprint.NextSupplyZoneId(), worldPosition = ToWorldPosition(worldPos) });
                break;
            case WorldObjectCategory.OrderStation:
                PlaceOrReplace(Blueprint.OrderStations, d => d.worldPosition.ToVector3(), worldPos,
                    () => new OrderStationData { id = Blueprint.NextOrderStationId(), worldPosition = ToWorldPosition(worldPos) });
                break;
            case WorldObjectCategory.ToolDepot:
                PlaceOrReplace(Blueprint.ToolDepots, d => d.worldPosition.ToVector3(), worldPos,
                    () => new ToolDepotData { id = Blueprint.NextToolDepotId(), worldPosition = ToWorldPosition(worldPos), tools = new[] { "hammer" } });
                break;
            case WorldObjectCategory.TrashBin:
                PlaceOrReplace(Blueprint.TrashBins, d => d.worldPosition.ToVector3(), worldPos,
                    () => new TrashBinData { id = Blueprint.NextTrashBinId(), worldPosition = ToWorldPosition(worldPos) });
                break;
            case WorldObjectCategory.PlayerSpawn:
                PlaceOrReplace(Blueprint.PlayerSpawns, d => d.ToVector3(), worldPos,
                    () => ToWorldPosition(worldPos), maxCount: 4);
                break;
        }
    }

    /// <summary>Replaces whatever's already within pickRadius of worldPos (so re-clicking an
    /// occupied spot swaps it instead of stacking a duplicate on top), or adds a new entry if
    /// the spot is empty. maxCount enforces PlayerSpawn's 4-player cap on the add path only --
    /// replacing an existing spawn never changes the count.</summary>
    private void PlaceOrReplace<T>(List<T> list, Func<T, Vector3> getPos, Vector3 target, Func<T> makeItem, int maxCount = int.MaxValue)
    {
        const float pickRadius = 1f;

        int existingIndex = -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (HorizontalDistance(getPos(list[i]), target) <= pickRadius)
            {
                existingIndex = i;
                break;
            }
        }

        if (existingIndex < 0)
        {
            if (list.Count >= maxCount) return;

            T item = makeItem();
            Commands.Run(new ActionCommand(
                execute: () => { list.Add(item); RefreshWorldObjectVisuals(); },
                undo: () => { list.Remove(item); RefreshWorldObjectVisuals(); }));
            return;
        }

        T previous = list[existingIndex];
        T replacement = makeItem();
        Commands.Run(new ActionCommand(
            execute: () => { list[existingIndex] = replacement; RefreshWorldObjectVisuals(); },
            undo: () => { list[existingIndex] = previous; RefreshWorldObjectVisuals(); }));
    }

    private void EraseNearestWorldObject(Vector3 worldPos)
    {
        const float pickRadius = 1f;

        switch (BrushCategory)
        {
            case WorldObjectCategory.SupplyZone:
                RemoveNearest(Blueprint.SupplyZones, d => d.worldPosition.ToVector3(), worldPos, pickRadius);
                break;
            case WorldObjectCategory.OrderStation:
                RemoveNearest(Blueprint.OrderStations, d => d.worldPosition.ToVector3(), worldPos, pickRadius);
                break;
            case WorldObjectCategory.ToolDepot:
                RemoveNearest(Blueprint.ToolDepots, d => d.worldPosition.ToVector3(), worldPos, pickRadius);
                break;
            case WorldObjectCategory.TrashBin:
                RemoveNearest(Blueprint.TrashBins, d => d.worldPosition.ToVector3(), worldPos, pickRadius);
                break;
            case WorldObjectCategory.PlayerSpawn:
                RemoveNearest(Blueprint.PlayerSpawns, d => d.ToVector3(), worldPos, pickRadius);
                break;
        }
    }

    private void RemoveNearest<T>(List<T> list, Func<T, Vector3> getPos, Vector3 target, float radius)
    {
        T closest = default;
        float bestDist = radius;
        bool found = false;

        foreach (T item in list)
        {
            float d = HorizontalDistance(getPos(item), target);
            if (d > bestDist) continue;
            bestDist = d;
            closest = item;
            found = true;
        }

        if (!found) return;

        int index = list.IndexOf(closest);
        Commands.Run(new ActionCommand(
            execute: () => { list.Remove(closest); RefreshWorldObjectVisuals(); },
            undo: () => { list.Insert(index, closest); RefreshWorldObjectVisuals(); }));
    }

    /// <summary>Ignores Y when matching an existing World Object under a click -- a top-down
    /// editor click can't express vertical depth anyway, and WorldObjectHeight being fixed at
    /// 1f shouldn't make pre-existing data saved at a different Y (e.g. 0.5) unmatchable.</summary>
    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static WorldPosition ToWorldPosition(Vector3 v) => new WorldPosition { x = v.x, y = v.y, z = v.z };

    // -------------------------------------------------------------------------
    // Visuals -- placeholder primitives, no art assets required.
    // -------------------------------------------------------------------------

    /// <summary>Recomputes every Wall tile's connection mask from scratch against the
    /// current Blueprint (Smart Wall System, Section 10). Called before every
    /// RefreshAllTileVisuals() so CreateTileCube always has up-to-date rotation data to
    /// read -- see _editorWallVariants for why this is a full pass instead of incremental.</summary>
    private void RebuildEditorWallVariants()
    {
        _editorWallVariants.Clear();

        foreach (var kvp in Blueprint.Tiles)
        {
            if (BlueprintEnums.ParseTileType(kvp.Value.type) != TileType.Wall) continue;

            Vector3Int pos = kvp.Key;
            byte mask = 0;
            if (GetBlueprintTypeAt(pos + Vector3Int.forward) == TileType.Wall) mask |= WallVariantLookup.North;
            if (GetBlueprintTypeAt(pos + Vector3Int.right) == TileType.Wall) mask |= WallVariantLookup.East;
            if (GetBlueprintTypeAt(pos + Vector3Int.back) == TileType.Wall) mask |= WallVariantLookup.South;
            if (GetBlueprintTypeAt(pos + Vector3Int.left) == TileType.Wall) mask |= WallVariantLookup.West;

            _editorWallVariants[pos] = WallVariantLookup.GetVariant(mask);
        }
    }

    private void RefreshAllTileVisuals()
    {
        foreach (GameObject go in _tileVisuals.Values) Destroy(go);
        _tileVisuals.Clear();

        foreach (var kvp in Blueprint.Tiles)
            _tileVisuals[kvp.Key] = CreateTileCube(kvp.Key, kvp.Value);
    }

    private void RefreshTileVisual(Vector3Int pos)
    {
        if (_tileVisuals.TryGetValue(pos, out GameObject existing))
        {
            Destroy(existing);
            _tileVisuals.Remove(pos);
        }

        if (Blueprint.Tiles.TryGetValue(pos, out TileData tile))
            _tileVisuals[pos] = CreateTileCube(pos, tile);
    }

    // Off-layer cubes shrink to this fraction of their on-layer size (matches the
    // placeholder cube's existing 0.4/0.85 ratio) -- applied to real wall meshes too so
    // the "other layers, dimmed and smaller" read stays consistent across both visuals.
    private const float OffLayerScaleRatio = 0.4f / 0.85f;

    private GameObject CreateTileCube(Vector3Int pos, TileData tile)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = $"Tile_{pos.x}_{pos.y}_{pos.z}";
        go.transform.SetParent(_visualsRoot);

        bool onCurrentLayer = pos.y == CurrentLayer;
        go.transform.position = new Vector3((pos.x + 0.5f) * CellSize, pos.y * CellSize, (pos.z + 0.5f) * CellSize);

        bool isWall = BlueprintEnums.ParseTileType(tile.type) == TileType.Wall;
        _editorWallVariants.TryGetValue(pos, out var wallVariant);

        // Smart Wall System (Section 10) -- swaps the cube for the real connection-shaped
        // mesh (same WallMeshSet BuildTile reads at runtime) so corner/T-junction/cross
        // runs read correctly here instead of every Wall tile looking like the same cube.
        Mesh wallMesh = isWall && wallMeshSet != null ? wallMeshSet.MeshFor(wallVariant.variant) : null;
        if (wallMesh != null)
        {
            go.GetComponent<MeshFilter>().sharedMesh = wallMesh;
            go.transform.localScale = Vector3.one * (onCurrentLayer ? 1f : OffLayerScaleRatio);
        }
        else
        {
            float scale = onCurrentLayer ? CellSize * 0.85f : CellSize * 0.4f;
            go.transform.localScale = Vector3.one * scale;
        }

        if (isWall)
            go.transform.localEulerAngles = new Vector3(0f, wallVariant.yRotation, 0f);

        Color color = TileTypeColors.ColorFor(BlueprintEnums.ParseTileType(tile.type));
        if (!onCurrentLayer) color = Color.Lerp(color, Color.black, 0.6f);

        go.GetComponent<MeshRenderer>().sharedMaterial = GetSharedMaterial(color);
        return go;
    }

    private static readonly Color SupplyZoneColor = Color.green;
    private static readonly Color OrderStationColor = Color.yellow;
    private static readonly Color ToolDepotColor = new(1f, 0.5f, 0f);
    private static readonly Color TrashBinColor = Color.red;
    private static readonly Color PlayerSpawnColor = Color.cyan;

    private void RefreshWorldObjectVisuals()
    {
        foreach (GameObject go in _worldObjectVisuals) Destroy(go);
        _worldObjectVisuals.Clear();

        foreach (SupplyZoneData zone in Blueprint.SupplyZones)
            _worldObjectVisuals.Add(CreateWorldObjectMarker(zone.worldPosition.ToVector3(), SupplyZoneColor));
        foreach (OrderStationData station in Blueprint.OrderStations)
            _worldObjectVisuals.Add(CreateWorldObjectMarker(station.worldPosition.ToVector3(), OrderStationColor));
        foreach (ToolDepotData depot in Blueprint.ToolDepots)
            _worldObjectVisuals.Add(CreateWorldObjectMarker(depot.worldPosition.ToVector3(), ToolDepotColor));
        foreach (TrashBinData bin in Blueprint.TrashBins)
            _worldObjectVisuals.Add(CreateWorldObjectMarker(bin.worldPosition.ToVector3(), TrashBinColor));
        foreach (WorldPosition spawn in Blueprint.PlayerSpawns)
            _worldObjectVisuals.Add(CreateWorldObjectMarker(spawn.ToVector3(), PlayerSpawnColor));
    }

    private GameObject CreateWorldObjectMarker(Vector3 pos, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.transform.SetParent(_visualsRoot);
        Destroy(go.GetComponent<Collider>()); // informational icon only -- shouldn't block Preview Mode movement
        go.transform.position = pos + Vector3.up;
        go.transform.localScale = Vector3.one * 0.6f;
        go.GetComponent<MeshRenderer>().sharedMaterial = GetSharedMaterial(color);
        return go;
    }

    private Material GetSharedMaterial(Color color)
    {
        if (_materialCache.TryGetValue(color, out Material mat)) return mat;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        mat = new Material(shader) { color = color };
        _materialCache[color] = mat;
        return mat;
    }

    // -------------------------------------------------------------------------
    // New / Load / Save
    // -------------------------------------------------------------------------

    public void NewBlueprint()
    {
        Blueprint = new EditableBlueprint();
        Commands.Clear();
        SelectedTilePos = null;
        CurrentLayer = 0;
        RefreshAllTileVisuals();
        RefreshWorldObjectVisuals();
        BlueprintChanged?.Invoke();
    }

    public void LoadBlueprint(string id)
    {
        BlueprintData data = BlueprintLoader.Load(id);
        if (data == null) return;

        Blueprint = EditableBlueprint.FromBlueprintData(data);
        Commands.Clear();
        SelectedTilePos = null;
        CurrentLayer = Mathf.Clamp(CurrentLayer, 0, Blueprint.GridSize.y - 1);
        RefreshAllTileVisuals();
        RefreshWorldObjectVisuals();
        BlueprintChanged?.Invoke();
    }

    /// <summary>Applied by LevelEditorBlueprintSync on non-host clients when the host's
    /// state changes -- same body as LoadBlueprint, minus the disk/cloud read since the
    /// data arrives over the network instead. Does not re-fire BlueprintChanged --
    /// nothing on a spectator client needs to react to its own incoming sync.</summary>
    public void ApplyRemoteBlueprint(BlueprintData data)
    {
        Blueprint = EditableBlueprint.FromBlueprintData(data);
        Commands.Clear();
        SelectedTilePos = null;
        CurrentLayer = Mathf.Clamp(CurrentLayer, 0, Blueprint.GridSize.y - 1);
        RebuildEditorWallVariants();
        RefreshAllTileVisuals();
        RefreshWorldObjectVisuals();
    }

    public string[] GetAvailableBlueprintIds() => BlueprintLoader.GetAllBlueprintIds();

    /// <summary>Writes to local StreamingAssets (Editor/dev builds) and Steam Cloud (so it's
    /// selectable from the Hub kiosk). Returns the normalized id that was saved under.</summary>
    public string SaveBlueprint()
    {
        if (!Blueprint.Id.StartsWith("blueprint_"))
            Blueprint.Id = "blueprint_" + Blueprint.Id;

        BlueprintData data = Blueprint.ToBlueprintData();
        string json = JsonConvert.SerializeObject(data, Formatting.Indented);

        string dir = Path.Combine(Application.streamingAssetsPath, "Blueprints");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, data.id + ".json"), json);

        BlueprintLoader.SaveToCloud(data);

        return data.id;
    }

    // -------------------------------------------------------------------------
    // Preview mode
    // -------------------------------------------------------------------------

    private void EnterPreviewMode()
    {
        if (Mode == EditorMode.Preview) return;

        Mode = EditorMode.Preview;
        editorCamera.gameObject.SetActive(false);
        if (pauseCanvas != null) pauseCanvas.SetActive(false);

        Vector3 spawnPos = Blueprint.PlayerSpawns.Count > 0
            ? Blueprint.PlayerSpawns[0].ToVector3() + Vector3.up
            : new Vector3(Blueprint.GridSize.x * CellSize * 0.5f, Blueprint.GridSize.y * CellSize + 2f, Blueprint.GridSize.z * CellSize * 0.5f);

        var previewGO = new GameObject("PreviewPlayer");
        previewGO.transform.position = spawnPos;
        _activePreview = previewGO.AddComponent<LevelEditorPreviewController>();
        _activePreview.Init(this);
    }

    public void ExitPreviewMode()
    {
        if (Mode != EditorMode.Preview) return;

        if (_activePreview != null) Destroy(_activePreview.gameObject);
        _activePreview = null;

        editorCamera.gameObject.SetActive(true);
        if (pauseCanvas != null) pauseCanvas.SetActive(true);
        Mode = EditorMode.Tiles;
    }
}
