using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// All Level Editor panels (Systems Architecture, Section 11), drawn via OnGUI --
/// same IMGUI convention PlayerInteraction already uses for the crosshair/order menu/
/// delivery queue, so the editor needs no Canvas/Button hierarchy to be usable. Uses
/// GUILayout (rather than PlayerInteraction's manually-positioned GUI.Box/Label calls)
/// since this UI has many more rows and benefits from automatic flow layout.
///
/// Setup: attach to the same GameObject as LevelEditorController and assign controller.
/// </summary>
public class LevelEditorUI : MonoBehaviour
{
    [SerializeField] private LevelEditorController controller;

    /// <summary>True while the mouse is over any editor panel -- LevelEditorController
    /// checks this before treating a click as a world-space tile/object placement.</summary>
    public static bool IsPointerOverUI { get; private set; }

    private static readonly string[] SceneryOptions = { "forest", "urban", "coastal", "desert" };
    private static readonly string[] MaterialOptions = { "wood", "concrete", "steel" };

    private bool _showLoadList;
    private string[] _availableIds = Array.Empty<string>();
    private string _statusMessage = "";

    private GUIStyle _boldLabel;
    private GUIStyle _centeredLabel;
    private bool _stylesReady;

    private readonly List<Rect> _panelRects = new();

    private void OnGUI()
    {
        EnsureStyles();
        _panelRects.Clear();

        if (controller.Mode == LevelEditorController.EditorMode.Preview)
        {
            GUI.Label(new Rect(10, 10, 400, 20), "Preview Mode -- WASD move, mouse look, ESC to exit");
            IsPointerOverUI = false;
            return;
        }

        DrawTopBar();
        if (controller.Mode == LevelEditorController.EditorMode.Tiles) DrawTilePalette();
        if (controller.Mode == LevelEditorController.EditorMode.WorldObjects) DrawWorldObjectPanel();
        DrawTilePropertyPanel();
        DrawContractSettings();
        DrawSaveLoadPanel();

        IsPointerOverUI = _panelRects.Any(r => r.Contains(Event.current.mousePosition));
    }

    private void EnsureStyles()
    {
        if (_stylesReady) return;
        _boldLabel = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        _centeredLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
        _stylesReady = true;
    }

    private Rect TrackArea(Rect rect)
    {
        _panelRects.Add(rect);
        return rect;
    }

    // -------------------------------------------------------------------------
    // Top bar -- mode switch, layer, undo/redo
    // -------------------------------------------------------------------------

    private void DrawTopBar()
    {
        var rect = TrackArea(new Rect(10, 10, Screen.width - 20, 30));
        GUILayout.BeginArea(rect);
        GUILayout.BeginHorizontal();

        DrawModeButton("Tiles", LevelEditorController.EditorMode.Tiles);
        DrawModeButton("World Objects", LevelEditorController.EditorMode.WorldObjects);
        if (GUILayout.Button("Preview", GUILayout.Width(80))) controller.SetMode(LevelEditorController.EditorMode.Preview);

        GUILayout.Space(20);
        GUILayout.Label($"Layer: {controller.CurrentLayer + 1}/{controller.Blueprint.GridSize.y}  ([ / ])", GUILayout.Width(170));
        GUILayout.FlexibleSpace();

        GUI.enabled = controller.Commands.CanUndo;
        if (GUILayout.Button("Undo (Ctrl+Z)", GUILayout.Width(100))) controller.Commands.Undo();
        GUI.enabled = controller.Commands.CanRedo;
        if (GUILayout.Button("Redo (Ctrl+Y)", GUILayout.Width(100))) controller.Commands.Redo();
        GUI.enabled = true;

        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private void DrawModeButton(string label, LevelEditorController.EditorMode mode)
    {
        string text = controller.Mode == mode ? $"[{label}]" : label;
        if (GUILayout.Button(text, GUILayout.Width(label == "Tiles" ? 80 : 110)))
            controller.SetMode(mode);
    }

    // -------------------------------------------------------------------------
    // Tile palette (left panel, Tiles mode)
    // -------------------------------------------------------------------------

    private void DrawTilePalette()
    {
        var rect = TrackArea(new Rect(10, 50, 260, 230));
        GUILayout.BeginArea(rect, GUI.skin.box);

        GUILayout.Label("Tile Palette", _boldLabel);
        DrawEnumRow("Type", controller.BrushTileType, v => controller.BrushTileType = v);
        DrawEnumRow("Material", controller.BrushMaterial, v => controller.BrushMaterial = v);
        DrawEnumRow("Tool", controller.BrushTool, v => controller.BrushTool = v);

        GUILayout.BeginHorizontal();
        GUILayout.Label($"Health: {controller.BrushHealth}", GUILayout.Width(110));
        if (GUILayout.Button("-10", GUILayout.Width(40))) controller.BrushHealth = Mathf.Max(1, controller.BrushHealth - 10);
        if (GUILayout.Button("+10", GUILayout.Width(40))) controller.BrushHealth += 10;
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        GUILayout.Label("Click: place/select tile");
        GUILayout.Label("Right-click: erase tile");

        GUILayout.EndArea();
    }

    // -------------------------------------------------------------------------
    // World object panel (left panel, WorldObjects mode)
    // -------------------------------------------------------------------------

    private void DrawWorldObjectPanel()
    {
        var rect = TrackArea(new Rect(10, 50, 280, 280));
        GUILayout.BeginArea(rect, GUI.skin.box);

        GUILayout.Label("World Objects", _boldLabel);
        DrawEnumRow("Category", controller.BrushCategory, v => controller.BrushCategory = v);

        GUILayout.Space(6);
        GUILayout.Label($"Supply Zones: {controller.Blueprint.SupplyZones.Count}");
        GUILayout.Label($"Order Stations: {controller.Blueprint.OrderStations.Count}");
        GUILayout.Label($"Tool Depots: {controller.Blueprint.ToolDepots.Count}");
        GUILayout.Label($"Player Spawns: {controller.Blueprint.PlayerSpawns.Count}/4");

        GUILayout.Space(6);
        GUILayout.Label("Click: place (selected category)");
        GUILayout.Label("Right-click: erase nearest");

        if (controller.Blueprint.ToolDepots.Count > 0)
        {
            GUILayout.Space(8);
            GUILayout.Label("Tool Depot Contents:", _boldLabel);
            foreach (ToolDepotData depot in controller.Blueprint.ToolDepots)
            {
                GUILayout.Label(depot.id);
                GUILayout.BeginHorizontal();
                DrawToolToggle(depot, "hammer");
                DrawToolToggle(depot, "trowel");
                DrawToolToggle(depot, "torch");
                GUILayout.EndHorizontal();
            }
        }

        GUILayout.EndArea();
    }

    private void DrawToolToggle(ToolDepotData depot, string toolName)
    {
        bool has = depot.tools != null && depot.tools.Contains(toolName);
        bool newVal = GUILayout.Toggle(has, toolName, GUILayout.Width(80));
        if (newVal == has) return;

        var list = depot.tools != null ? depot.tools.ToList() : new List<string>();
        if (newVal) list.Add(toolName);
        else list.Remove(toolName);
        depot.tools = list.ToArray();
    }

    // -------------------------------------------------------------------------
    // Tile property panel (right panel, only when a tile is selected)
    // -------------------------------------------------------------------------

    private void DrawTilePropertyPanel()
    {
        TileData tile = controller.GetSelectedTile();
        if (tile == null) return;

        var rect = TrackArea(new Rect(Screen.width - 280, 50, 270, 230));
        GUILayout.BeginArea(rect, GUI.skin.box);

        GUILayout.Label($"Tile: {tile.id}", _boldLabel);
        GUILayout.Label($"Position: ({tile.position.x}, {tile.position.y}, {tile.position.z})");

        DrawEnumRow("Type", BlueprintEnums.ParseTileType(tile.type), controller.SetSelectedTileType);
        DrawEnumRow("Material", BlueprintEnums.ParseMaterialType(tile.requiredMaterial), controller.SetSelectedTileMaterial);
        DrawEnumRow("Tool", BlueprintEnums.ParseToolType(tile.requiredTool), controller.SetSelectedTileTool);

        GUILayout.BeginHorizontal();
        GUILayout.Label($"Health: {tile.health}", GUILayout.Width(110));
        if (GUILayout.Button("-10", GUILayout.Width(40))) controller.SetSelectedTileHealth(tile.health - 10);
        if (GUILayout.Button("+10", GUILayout.Width(40))) controller.SetSelectedTileHealth(tile.health + 10);
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Deselect")) controller.DeselectTile();

        GUILayout.EndArea();
    }

    // -------------------------------------------------------------------------
    // Contract settings (bottom-left, always visible)
    // -------------------------------------------------------------------------

    private void DrawContractSettings()
    {
        var rect = TrackArea(new Rect(10, Screen.height - 230, 420, 220));
        GUILayout.BeginArea(rect, GUI.skin.box);

        GUILayout.Label("Contract Settings", _boldLabel);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Id:", GUILayout.Width(70));
        controller.Blueprint.Id = GUILayout.TextField(controller.Blueprint.Id, GUILayout.Width(150));
        GUILayout.Label("Name:", GUILayout.Width(50));
        controller.Blueprint.Name = GUILayout.TextField(controller.Blueprint.Name, GUILayout.Width(120));
        GUILayout.EndHorizontal();

        DrawStringCycleRow("Scenery", SceneryOptions, controller.Blueprint.Scenery, v => controller.Blueprint.Scenery = v);

        GUILayout.BeginHorizontal();
        GUILayout.Label($"Time Limit: {controller.Blueprint.TimeLimitSeconds}s", GUILayout.Width(140));
        if (GUILayout.Button("-30", GUILayout.Width(40))) controller.Blueprint.TimeLimitSeconds = Mathf.Max(30, controller.Blueprint.TimeLimitSeconds - 30);
        if (GUILayout.Button("+30", GUILayout.Width(40))) controller.Blueprint.TimeLimitSeconds += 30;
        GUILayout.EndHorizontal();

        GUILayout.Label("Allowed Materials:");
        GUILayout.BeginHorizontal();
        foreach (string material in MaterialOptions)
            DrawMaterialToggle(material);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        DrawFloatStepper("Full", () => controller.Blueprint.CompletionFull, v => controller.Blueprint.CompletionFull = v);
        DrawFloatStepper("Partial", () => controller.Blueprint.CompletionPartial, v => controller.Blueprint.CompletionPartial = v);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label($"Base Payout: {controller.Blueprint.BasePayout}", GUILayout.Width(140));
        if (GUILayout.Button("-50", GUILayout.Width(40))) controller.Blueprint.BasePayout = Mathf.Max(0, controller.Blueprint.BasePayout - 50);
        if (GUILayout.Button("+50", GUILayout.Width(40))) controller.Blueprint.BasePayout += 50;
        GUILayout.EndHorizontal();

        DrawGridSizeRow();

        GUILayout.EndArea();
    }

    private void DrawMaterialToggle(string material)
    {
        bool has = controller.Blueprint.AllowedMaterials.Contains(material);
        bool newVal = GUILayout.Toggle(has, material, GUILayout.Width(90));
        if (newVal == has) return;

        if (newVal) controller.Blueprint.AllowedMaterials.Add(material);
        else controller.Blueprint.AllowedMaterials.Remove(material);
    }

    private void DrawFloatStepper(string label, Func<float> get, Action<float> set)
    {
        GUILayout.Label($"{label}: {get():0.00}", GUILayout.Width(90));
        if (GUILayout.Button("-", GUILayout.Width(24))) set(Mathf.Clamp(get() - 0.05f, 0f, 1f));
        if (GUILayout.Button("+", GUILayout.Width(24))) set(Mathf.Clamp(get() + 0.05f, 0f, 1f));
    }

    private void DrawGridSizeRow()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Grid Size:", GUILayout.Width(70));

        DrawAxisStepper("X", controller.Blueprint.GridSize.x, v =>
        {
            var size = controller.Blueprint.GridSize;
            size.x = v;
            controller.Blueprint.GridSize = size;
        });
        DrawAxisStepper("Y", controller.Blueprint.GridSize.y, v =>
        {
            var size = controller.Blueprint.GridSize;
            size.y = v;
            controller.Blueprint.GridSize = size;
            controller.SetLayer(controller.CurrentLayer);
        });
        DrawAxisStepper("Z", controller.Blueprint.GridSize.z, v =>
        {
            var size = controller.Blueprint.GridSize;
            size.z = v;
            controller.Blueprint.GridSize = size;
        });

        GUILayout.EndHorizontal();
    }

    private void DrawAxisStepper(string label, int value, Action<int> set)
    {
        GUILayout.Label($"{label}:{value}", GUILayout.Width(40));
        if (GUILayout.Button("-", GUILayout.Width(20))) set(Mathf.Max(1, value - 1));
        if (GUILayout.Button("+", GUILayout.Width(20))) set(value + 1);
    }

    // -------------------------------------------------------------------------
    // Save / load (bottom-right)
    // -------------------------------------------------------------------------

    private void DrawSaveLoadPanel()
    {
        var rect = TrackArea(new Rect(Screen.width - 280, Screen.height - 110, 270, 100));
        GUILayout.BeginArea(rect, GUI.skin.box);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("New")) { controller.NewBlueprint(); _statusMessage = "New blueprint."; }
        if (GUILayout.Button("Save"))
        {
            string savedId = controller.SaveBlueprint();
            _statusMessage = $"Saved as {savedId}";
        }
        if (GUILayout.Button("Load"))
        {
            _showLoadList = !_showLoadList;
            _availableIds = controller.GetAvailableBlueprintIds();
        }
        GUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(_statusMessage)) GUILayout.Label(_statusMessage);

        GUILayout.EndArea();

        if (_showLoadList) DrawLoadList();
    }

    private void DrawLoadList()
    {
        float height = 40 + _availableIds.Length * 26;
        var rect = TrackArea(new Rect(Screen.width - 280, Screen.height - 120 - height, 270, height));
        GUILayout.BeginArea(rect, GUI.skin.box);

        GUILayout.Label("Load Blueprint", _boldLabel);
        foreach (string id in _availableIds)
        {
            if (GUILayout.Button(id))
            {
                controller.LoadBlueprint(id);
                _statusMessage = $"Loaded {id}";
                _showLoadList = false;
            }
        }
        if (GUILayout.Button("Cancel")) _showLoadList = false;

        GUILayout.EndArea();
    }

    // -------------------------------------------------------------------------
    // Shared widgets
    // -------------------------------------------------------------------------

    private void DrawEnumRow<T>(string label, T value, Action<T> onChange) where T : Enum
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(70));
        if (GUILayout.Button("<", GUILayout.Width(24))) onChange(CycleEnum(value, -1));
        GUILayout.Label(value.ToString(), _centeredLabel, GUILayout.Width(90));
        if (GUILayout.Button(">", GUILayout.Width(24))) onChange(CycleEnum(value, 1));
        GUILayout.EndHorizontal();
    }

    private void DrawStringCycleRow(string label, string[] options, string value, Action<string> onChange)
    {
        int idx = Math.Max(0, Array.IndexOf(options, value));

        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(70));
        if (GUILayout.Button("<", GUILayout.Width(24))) onChange(options[(idx - 1 + options.Length) % options.Length]);
        GUILayout.Label(value, _centeredLabel, GUILayout.Width(90));
        if (GUILayout.Button(">", GUILayout.Width(24))) onChange(options[(idx + 1) % options.Length]);
        GUILayout.EndHorizontal();
    }

    private static T CycleEnum<T>(T current, int direction) where T : Enum
    {
        var values = (T[])Enum.GetValues(typeof(T));
        int idx = Array.IndexOf(values, current);
        idx = (idx + direction + values.Length) % values.Length;
        return values[idx];
    }
}
