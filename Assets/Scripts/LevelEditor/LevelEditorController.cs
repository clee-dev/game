using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Top-level orchestrator for the Level Editor (Systems Architecture, Section 11): owns
/// the in-memory EditableBlueprint, the undo/redo command stack, the current mode/Y-layer/
/// brush, and the placeholder visuals (primitive cubes/spheres -- no art needed). All
/// mutations to Blueprint go through Commands.Run() so they're undoable.
///
/// Click-to-grid-cell targeting is plain math against the current layer's horizontal
/// plane (LevelEditorCamera.ScreenToPlane), not physics raycasting against the tile
/// visuals -- there's no need for the visuals to carry colliders for editing input. Tile
/// cubes keep their default BoxCollider anyway, since Preview Mode's CharacterController
/// needs something solid to stand on/bump into.
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
    public enum WorldObjectCategory { SupplyZone, OrderStation, ToolDepot, PlayerSpawn }

    public const float CellSize = BuildSystem.CellSize;

    [SerializeField] private LevelEditorCamera editorCamera;

    public EditableBlueprint Blueprint { get; private set; } = new();
    public EditorCommandStack Commands { get; } = new();

    public EditorMode Mode { get; private set; } = EditorMode.Tiles;
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

    private LevelEditorPreviewController _activePreview;

    private void Awake()
    {
        _visualsRoot = new GameObject("EditorVisuals").transform;
        _visualsRoot.SetParent(transform);
        RefreshAllTileVisuals();
        RefreshWorldObjectVisuals();
    }

    private void Update()
    {
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
        Vector3 groundHit = editorCamera.ScreenToPlane(Mouse.current.position.ReadValue(), CurrentLayer * CellSize);

        if (Mode == EditorMode.Tiles)
        {
            Vector3Int? gridPos = WorldToGridPos(groundHit);
            if (gridPos == null) return;

            if (erase) EraseTile(gridPos.Value);
            else PlaceOrSelectTile(gridPos.Value);
        }
        else if (Mode == EditorMode.WorldObjects)
        {
            if (erase) EraseNearestWorldObject(groundHit);
            else PlaceWorldObject(groundHit);
        }
    }

    private Vector3Int? WorldToGridPos(Vector3 worldPos)
    {
        int gx = Mathf.FloorToInt(worldPos.x / CellSize);
        int gz = Mathf.FloorToInt(worldPos.z / CellSize);
        if (gx < 0 || gx >= Blueprint.GridSize.x || gz < 0 || gz >= Blueprint.GridSize.z) return null;

        return new Vector3Int(gx, CurrentLayer, gz);
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
                AddWorldObject(Blueprint.SupplyZones, new SupplyZoneData { id = Blueprint.NextSupplyZoneId(), worldPosition = ToWorldPosition(worldPos) });
                break;
            case WorldObjectCategory.OrderStation:
                AddWorldObject(Blueprint.OrderStations, new OrderStationData { id = Blueprint.NextOrderStationId(), worldPosition = ToWorldPosition(worldPos) });
                break;
            case WorldObjectCategory.ToolDepot:
                AddWorldObject(Blueprint.ToolDepots, new ToolDepotData { id = Blueprint.NextToolDepotId(), worldPosition = ToWorldPosition(worldPos), tools = new[] { "hammer" } });
                break;
            case WorldObjectCategory.PlayerSpawn:
                if (Blueprint.PlayerSpawns.Count < 4)
                    AddWorldObject(Blueprint.PlayerSpawns, ToWorldPosition(worldPos));
                break;
        }
    }

    private void AddWorldObject<T>(List<T> list, T item)
    {
        Commands.Run(new ActionCommand(
            execute: () => { list.Add(item); RefreshWorldObjectVisuals(); },
            undo: () => { list.Remove(item); RefreshWorldObjectVisuals(); }));
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
            float d = Vector3.Distance(getPos(item), target);
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

    private static WorldPosition ToWorldPosition(Vector3 v) => new WorldPosition { x = v.x, y = v.y, z = v.z };

    // -------------------------------------------------------------------------
    // Visuals -- placeholder primitives, no art assets required.
    // -------------------------------------------------------------------------

    private static readonly Dictionary<TileType, Color> TileColors = new()
    {
        { TileType.Foundation, new Color(0.5f, 0.5f, 0.5f) },
        { TileType.Floor,      new Color(0.6f, 0.4f, 0.2f) },
        { TileType.Wall,       new Color(0.8f, 0.7f, 0.5f) },
        { TileType.Window,     new Color(0.6f, 0.8f, 1f) },
        { TileType.Door,       new Color(0.9f, 0.5f, 0.1f) },
        { TileType.Column,     new Color(0.3f, 0.3f, 0.3f) },
        { TileType.Furniture,  new Color(0.3f, 0.7f, 0.3f) },
        { TileType.Decor,      new Color(0.6f, 0.3f, 0.7f) },
    };

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

    private GameObject CreateTileCube(Vector3Int pos, TileData tile)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = $"Tile_{pos.x}_{pos.y}_{pos.z}";
        go.transform.SetParent(_visualsRoot);

        bool onCurrentLayer = pos.y == CurrentLayer;
        float scale = onCurrentLayer ? CellSize * 0.85f : CellSize * 0.4f;
        go.transform.position = new Vector3((pos.x + 0.5f) * CellSize, pos.y * CellSize, (pos.z + 0.5f) * CellSize);
        go.transform.localScale = Vector3.one * scale;

        Color color = TileColors.TryGetValue(BlueprintEnums.ParseTileType(tile.type), out Color c) ? c : Color.white;
        if (!onCurrentLayer) color = Color.Lerp(color, Color.black, 0.6f);

        go.GetComponent<MeshRenderer>().sharedMaterial = GetSharedMaterial(color);
        return go;
    }

    private static readonly Color SupplyZoneColor = Color.green;
    private static readonly Color OrderStationColor = Color.yellow;
    private static readonly Color ToolDepotColor = new(1f, 0.5f, 0f);
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
        Mode = EditorMode.Tiles;
    }
}
