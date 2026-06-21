using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Canvas/uGUI replacement for LevelEditorUI's OnGUI panels (Systems Architecture,
/// Section 11). Same responsibilities as LevelEditorUI -- mode switch, tile palette,
/// world object panel, tile property panel, contract settings, save/load -- but driven
/// by real Button/Toggle/TMP_InputField widgets instead of immediate-mode GUI calls,
/// for project-wide UI consistency with the Order/Kiosk/Terminal menu pattern
/// (a thin always-active "panel" script that refreshes widget state from the
/// underlying data every frame).
///
/// Buttons use real Button.OnClick persistent listeners (static int arguments where
/// needed, same as KioskMenuPanel/TerminalMenuPanel). Toggles and TMP_InputFields are
/// NOT event-wired -- Unity's dynamic (pass-the-actual-runtime-value) persistent
/// listener mode isn't reachable through UnityEventTools' public API, only static
/// arguments are. Instead they're pull-synced every frame in Update() via
/// SyncToggle/SyncField, which diff the widget's current value against a cached
/// "last known" value to tell a user edit (widget changed, push to blueprint) apart
/// from an external change (blueprint changed via New/Load, pull into widget).
///
/// Setup: attach to the same GameObject as LevelEditorController and assign controller.
/// </summary>
public class LevelEditorCanvasUI : MonoBehaviour
{
    [SerializeField] private LevelEditorController controller;

    [Header("Top bar")]
    [SerializeField] private GameObject topBar;
    [SerializeField] private Button tilesModeButton;
    [SerializeField] private Button worldObjectsModeButton;
    [SerializeField] private Button previewModeButton;
    [SerializeField] private Button layerModeButton;
    [SerializeField] private TextMeshProUGUI layerModeLabel;
    [SerializeField] private Button layerPrevButton;
    [SerializeField] private Button layerNextButton;
    [SerializeField] private TextMeshProUGUI layerLabel;
    [SerializeField] private TextMeshProUGUI placementWarningLabel;
    [SerializeField] private Button undoButton;
    [SerializeField] private Button redoButton;
    [SerializeField] private GameObject spectatingLabel;
    [SerializeField] private GameObject previewModeBanner;

    [Header("Tile palette")]
    [SerializeField] private GameObject tilePalettePanel;
    [SerializeField] private TextMeshProUGUI tileTypeLabel;
    [SerializeField] private TextMeshProUGUI tileMaterialLabel;
    [SerializeField] private TextMeshProUGUI tileToolLabel;
    [SerializeField] private TextMeshProUGUI tileHealthLabel;

    [Header("World object panel")]
    [SerializeField] private GameObject worldObjectPanel;
    [SerializeField] private TextMeshProUGUI categoryLabel;
    [SerializeField] private TextMeshProUGUI worldObjectCountsLabel;
    [SerializeField] private GameObject[] toolDepotRows;
    [SerializeField] private TextMeshProUGUI[] toolDepotIdLabels;
    [SerializeField] private Toggle[] toolDepotHammerToggles;
    [SerializeField] private Toggle[] toolDepotTrowelToggles;
    [SerializeField] private Toggle[] toolDepotTorchToggles;

    [Header("Tile property panel")]
    [SerializeField] private GameObject tilePropertyPanel;
    [SerializeField] private TextMeshProUGUI tilePropertyInfoLabel;
    [SerializeField] private TextMeshProUGUI propTypeLabel;
    [SerializeField] private TextMeshProUGUI propMaterialLabel;
    [SerializeField] private TextMeshProUGUI propToolLabel;
    [SerializeField] private TextMeshProUGUI propHealthLabel;

    [Header("Contract settings")]
    [SerializeField] private TMP_InputField idField;
    [SerializeField] private TMP_InputField nameField;
    [SerializeField] private TextMeshProUGUI sceneryLabel;
    [SerializeField] private TextMeshProUGUI timeLimitLabel;
    [SerializeField] private Toggle woodToggle;
    [SerializeField] private Toggle concreteToggle;
    [SerializeField] private Toggle steelToggle;
    [SerializeField] private TextMeshProUGUI completionFullLabel;
    [SerializeField] private TextMeshProUGUI completionPartialLabel;
    [SerializeField] private TextMeshProUGUI payoutLabel;
    [SerializeField] private TextMeshProUGUI gridXLabel;
    [SerializeField] private TextMeshProUGUI gridYLabel;
    [SerializeField] private TextMeshProUGUI gridZLabel;

    [Header("Save / load")]
    [SerializeField] private TextMeshProUGUI statusLabel;
    [SerializeField] private GameObject loadListPanel;
    [SerializeField] private GameObject[] loadListRows;
    [SerializeField] private TextMeshProUGUI[] loadListLabels;

    private static readonly string[] SceneryOptions = { "forest", "urban", "coastal", "desert" };

    private bool _showLoadList;
    private string[] _availableIds = Array.Empty<string>();
    private string _statusMessage = "";

    private readonly Dictionary<Toggle, bool> _toggleCache = new();
    private readonly Dictionary<TMP_InputField, string> _fieldCache = new();

    private void Update()
    {
        bool hostEditing = controller.IsHost && controller.Mode != LevelEditorController.EditorMode.Preview;

        spectatingLabel.SetActive(!controller.IsHost);
        previewModeBanner.SetActive(controller.IsHost && controller.Mode == LevelEditorController.EditorMode.Preview);
        topBar.SetActive(hostEditing);
        if (!hostEditing)
        {
            tilePalettePanel.SetActive(false);
            worldObjectPanel.SetActive(false);
            tilePropertyPanel.SetActive(false);
            loadListPanel.SetActive(false);
            return;
        }

        RefreshTopBar();
        tilePalettePanel.SetActive(controller.Mode == LevelEditorController.EditorMode.Tiles);
        if (controller.Mode == LevelEditorController.EditorMode.Tiles) RefreshTilePalette();

        worldObjectPanel.SetActive(controller.Mode == LevelEditorController.EditorMode.WorldObjects);
        if (controller.Mode == LevelEditorController.EditorMode.WorldObjects) RefreshWorldObjectPanel();

        TileData selected = controller.GetSelectedTile();
        tilePropertyPanel.SetActive(selected != null);
        if (selected != null) RefreshTilePropertyPanel(selected);

        RefreshContractSettings();

        if (!string.IsNullOrEmpty(_statusMessage)) statusLabel.text = _statusMessage;
        loadListPanel.SetActive(_showLoadList);
        if (_showLoadList) RefreshLoadList();
    }

    // -------------------------------------------------------------------------
    // Pull-sync helpers -- diff the widget's current value against the last
    // value we set, to tell "user just edited this" apart from "the underlying
    // data changed externally" without needing dynamic UnityEvent listeners.
    // -------------------------------------------------------------------------

    private void SyncToggle(Toggle toggle, bool blueprintValue, Action<bool> setBlueprint)
    {
        bool cached = _toggleCache.TryGetValue(toggle, out bool c) ? c : blueprintValue;
        if (toggle.isOn != cached)
        {
            setBlueprint(toggle.isOn);
            _toggleCache[toggle] = toggle.isOn;
        }
        else if (toggle.isOn != blueprintValue)
        {
            toggle.isOn = blueprintValue;
            _toggleCache[toggle] = blueprintValue;
        }
        else
        {
            _toggleCache[toggle] = toggle.isOn;
        }
    }

    private void SyncField(TMP_InputField field, string blueprintValue, Action<string> setBlueprint)
    {
        string cached = _fieldCache.TryGetValue(field, out string c) ? c : blueprintValue;
        if (field.text != cached)
        {
            setBlueprint(field.text);
            _fieldCache[field] = field.text;
        }
        else if (field.text != blueprintValue)
        {
            field.text = blueprintValue;
            _fieldCache[field] = blueprintValue;
        }
        else
        {
            _fieldCache[field] = field.text;
        }
    }

    // -------------------------------------------------------------------------
    // Top bar
    // -------------------------------------------------------------------------

    private void RefreshTopBar()
    {
        layerModeLabel.text = $"Layer Mode: {controller.CurrentLayerMode}";
        layerLabel.text = $"Layer: {controller.CurrentLayer + 1}/{controller.Blueprint.GridSize.y}";
        placementWarningLabel.text = controller.PlacementWarning ?? "";
        placementWarningLabel.gameObject.SetActive(!string.IsNullOrEmpty(controller.PlacementWarning));
        undoButton.interactable = controller.Commands.CanUndo;
        redoButton.interactable = controller.Commands.CanRedo;
    }

    public void OnTilesModeClicked() => controller.SetMode(LevelEditorController.EditorMode.Tiles);
    public void OnWorldObjectsModeClicked() => controller.SetMode(LevelEditorController.EditorMode.WorldObjects);
    public void OnPreviewModeClicked() => controller.SetMode(LevelEditorController.EditorMode.Preview);

    public void OnLayerModeToggleClicked() => controller.SetLayerMode(
        controller.CurrentLayerMode == LevelEditorController.LayerMode.Auto
            ? LevelEditorController.LayerMode.Manual
            : LevelEditorController.LayerMode.Auto);

    public void OnLayerPrevClicked() => controller.SetLayer(controller.CurrentLayer - 1);
    public void OnLayerNextClicked() => controller.SetLayer(controller.CurrentLayer + 1);
    public void OnUndoClicked() => controller.Commands.Undo();
    public void OnRedoClicked() => controller.Commands.Redo();

    // -------------------------------------------------------------------------
    // Tile palette
    // -------------------------------------------------------------------------

    private void RefreshTilePalette()
    {
        tileTypeLabel.text = controller.BrushTileType.ToString();
        tileMaterialLabel.text = controller.BrushMaterial.ToString();
        tileToolLabel.text = controller.BrushTool.ToString();
        tileHealthLabel.text = $"Health: {controller.BrushHealth}";
    }

    public void OnBrushTypePrev() => controller.BrushTileType = CycleEnum(controller.BrushTileType, -1);
    public void OnBrushTypeNext() => controller.BrushTileType = CycleEnum(controller.BrushTileType, 1);
    public void OnBrushMaterialPrev() => controller.BrushMaterial = CycleEnum(controller.BrushMaterial, -1);
    public void OnBrushMaterialNext() => controller.BrushMaterial = CycleEnum(controller.BrushMaterial, 1);
    public void OnBrushToolPrev() => controller.BrushTool = CycleEnum(controller.BrushTool, -1);
    public void OnBrushToolNext() => controller.BrushTool = CycleEnum(controller.BrushTool, 1);
    public void OnBrushHealthMinus() => controller.BrushHealth = Mathf.Max(1, controller.BrushHealth - 10);
    public void OnBrushHealthPlus() => controller.BrushHealth += 10;

    // -------------------------------------------------------------------------
    // World object panel
    // -------------------------------------------------------------------------

    private void RefreshWorldObjectPanel()
    {
        categoryLabel.text = controller.BrushCategory.ToString();
        worldObjectCountsLabel.text =
            $"Supply Zones: {controller.Blueprint.SupplyZones.Count}\n" +
            $"Order Stations: {controller.Blueprint.OrderStations.Count}\n" +
            $"Tool Depots: {controller.Blueprint.ToolDepots.Count}\n" +
            $"Trash Bins: {controller.Blueprint.TrashBins.Count}\n" +
            $"Player Spawns: {controller.Blueprint.PlayerSpawns.Count}/4";

        int depotCount = Mathf.Min(controller.Blueprint.ToolDepots.Count, toolDepotRows.Length);
        for (int i = 0; i < toolDepotRows.Length; i++)
        {
            bool active = i < depotCount;
            toolDepotRows[i].SetActive(active);
            if (!active) continue;

            ToolDepotData depot = controller.Blueprint.ToolDepots[i];
            toolDepotIdLabels[i].text = depot.id;
            SyncToggle(toolDepotHammerToggles[i], HasTool(depot, "hammer"), v => SetTool(depot, "hammer", v));
            SyncToggle(toolDepotTrowelToggles[i], HasTool(depot, "trowel"), v => SetTool(depot, "trowel", v));
            SyncToggle(toolDepotTorchToggles[i], HasTool(depot, "torch"), v => SetTool(depot, "torch", v));
        }
    }

    private static bool HasTool(ToolDepotData depot, string tool) => depot.tools != null && depot.tools.Contains(tool);

    private static void SetTool(ToolDepotData depot, string toolName, bool value)
    {
        List<string> list = depot.tools != null ? depot.tools.ToList() : new List<string>();
        if (value) { if (!list.Contains(toolName)) list.Add(toolName); }
        else list.Remove(toolName);
        depot.tools = list.ToArray();
    }

    public void OnBrushCategoryPrev() => controller.BrushCategory = CycleEnum(controller.BrushCategory, -1);
    public void OnBrushCategoryNext() => controller.BrushCategory = CycleEnum(controller.BrushCategory, 1);

    // -------------------------------------------------------------------------
    // Tile property panel
    // -------------------------------------------------------------------------

    private void RefreshTilePropertyPanel(TileData tile)
    {
        tilePropertyInfoLabel.text = $"Tile: {tile.id}\nPosition: ({tile.position.x}, {tile.position.y}, {tile.position.z})";
        propTypeLabel.text = tile.type;
        propMaterialLabel.text = tile.requiredMaterial;
        propToolLabel.text = tile.requiredTool;
        propHealthLabel.text = $"Health: {tile.health}";
    }

    public void OnPropTypePrev() { TileData t = controller.GetSelectedTile(); if (t != null) controller.SetSelectedTileType(CycleEnum(BlueprintEnums.ParseTileType(t.type), -1)); }
    public void OnPropTypeNext() { TileData t = controller.GetSelectedTile(); if (t != null) controller.SetSelectedTileType(CycleEnum(BlueprintEnums.ParseTileType(t.type), 1)); }
    public void OnPropMaterialPrev() { TileData t = controller.GetSelectedTile(); if (t != null) controller.SetSelectedTileMaterial(CycleEnum(BlueprintEnums.ParseMaterialType(t.requiredMaterial), -1)); }
    public void OnPropMaterialNext() { TileData t = controller.GetSelectedTile(); if (t != null) controller.SetSelectedTileMaterial(CycleEnum(BlueprintEnums.ParseMaterialType(t.requiredMaterial), 1)); }
    public void OnPropToolPrev() { TileData t = controller.GetSelectedTile(); if (t != null) controller.SetSelectedTileTool(CycleEnum(BlueprintEnums.ParseToolType(t.requiredTool), -1)); }
    public void OnPropToolNext() { TileData t = controller.GetSelectedTile(); if (t != null) controller.SetSelectedTileTool(CycleEnum(BlueprintEnums.ParseToolType(t.requiredTool), 1)); }
    public void OnPropHealthMinus() { TileData t = controller.GetSelectedTile(); if (t != null) controller.SetSelectedTileHealth(t.health - 10); }
    public void OnPropHealthPlus() { TileData t = controller.GetSelectedTile(); if (t != null) controller.SetSelectedTileHealth(t.health + 10); }
    public void OnDeselectClicked() => controller.DeselectTile();

    // -------------------------------------------------------------------------
    // Contract settings
    // -------------------------------------------------------------------------

    private void RefreshContractSettings()
    {
        SyncField(idField, controller.Blueprint.Id, v => controller.Blueprint.Id = v);
        SyncField(nameField, controller.Blueprint.Name, v => controller.Blueprint.Name = v);

        sceneryLabel.text = controller.Blueprint.Scenery;
        timeLimitLabel.text = $"Time Limit: {controller.Blueprint.TimeLimitSeconds}s";
        SyncToggle(woodToggle, controller.Blueprint.AllowedMaterials.Contains("wood"), v => SetAllowedMaterial("wood", v));
        SyncToggle(concreteToggle, controller.Blueprint.AllowedMaterials.Contains("concrete"), v => SetAllowedMaterial("concrete", v));
        SyncToggle(steelToggle, controller.Blueprint.AllowedMaterials.Contains("steel"), v => SetAllowedMaterial("steel", v));
        completionFullLabel.text = $"Full: {controller.Blueprint.CompletionFull:0.00}";
        completionPartialLabel.text = $"Partial: {controller.Blueprint.CompletionPartial:0.00}";
        payoutLabel.text = $"Base Payout: {controller.Blueprint.BasePayout}";
        gridXLabel.text = $"X:{controller.Blueprint.GridSize.x}";
        gridYLabel.text = $"Y:{controller.Blueprint.GridSize.y}";
        gridZLabel.text = $"Z:{controller.Blueprint.GridSize.z}";
    }

    public void OnSceneryPrev() => CycleScenery(-1);
    public void OnSceneryNext() => CycleScenery(1);

    private void CycleScenery(int direction)
    {
        int idx = Math.Max(0, Array.IndexOf(SceneryOptions, controller.Blueprint.Scenery));
        controller.Blueprint.Scenery = SceneryOptions[(idx + direction + SceneryOptions.Length) % SceneryOptions.Length];
    }

    public void OnTimeLimitMinus() => controller.Blueprint.TimeLimitSeconds = Mathf.Max(30, controller.Blueprint.TimeLimitSeconds - 30);
    public void OnTimeLimitPlus() => controller.Blueprint.TimeLimitSeconds += 30;

    private void SetAllowedMaterial(string material, bool allowed)
    {
        if (allowed) { if (!controller.Blueprint.AllowedMaterials.Contains(material)) controller.Blueprint.AllowedMaterials.Add(material); }
        else controller.Blueprint.AllowedMaterials.Remove(material);
    }

    public void OnCompletionFullMinus() => controller.Blueprint.CompletionFull = Mathf.Clamp(controller.Blueprint.CompletionFull - 0.05f, 0f, 1f);
    public void OnCompletionFullPlus() => controller.Blueprint.CompletionFull = Mathf.Clamp(controller.Blueprint.CompletionFull + 0.05f, 0f, 1f);
    public void OnCompletionPartialMinus() => controller.Blueprint.CompletionPartial = Mathf.Clamp(controller.Blueprint.CompletionPartial - 0.05f, 0f, 1f);
    public void OnCompletionPartialPlus() => controller.Blueprint.CompletionPartial = Mathf.Clamp(controller.Blueprint.CompletionPartial + 0.05f, 0f, 1f);
    public void OnPayoutMinus() => controller.Blueprint.BasePayout = Mathf.Max(0, controller.Blueprint.BasePayout - 50);
    public void OnPayoutPlus() => controller.Blueprint.BasePayout += 50;

    public void OnGridXMinus() => SetGridSize(Mathf.Max(1, controller.Blueprint.GridSize.x - 1), controller.Blueprint.GridSize.y, controller.Blueprint.GridSize.z);
    public void OnGridXPlus() => SetGridSize(controller.Blueprint.GridSize.x + 1, controller.Blueprint.GridSize.y, controller.Blueprint.GridSize.z);
    public void OnGridYMinus() => SetGridSizeY(Mathf.Max(1, controller.Blueprint.GridSize.y - 1));
    public void OnGridYPlus() => SetGridSizeY(controller.Blueprint.GridSize.y + 1);
    public void OnGridZMinus() => SetGridSize(controller.Blueprint.GridSize.x, controller.Blueprint.GridSize.y, Mathf.Max(1, controller.Blueprint.GridSize.z - 1));
    public void OnGridZPlus() => SetGridSize(controller.Blueprint.GridSize.x, controller.Blueprint.GridSize.y, controller.Blueprint.GridSize.z + 1);

    private void SetGridSize(int x, int y, int z) => controller.Blueprint.GridSize = new Vector3Int(x, y, z);

    private void SetGridSizeY(int y)
    {
        Vector3Int size = controller.Blueprint.GridSize;
        size.y = y;
        controller.Blueprint.GridSize = size;
        controller.SetLayer(controller.CurrentLayer);
    }

    // -------------------------------------------------------------------------
    // Save / load
    // -------------------------------------------------------------------------

    public void OnNewClicked()
    {
        controller.NewBlueprint();
        _statusMessage = "New blueprint.";
    }

    public void OnSaveClicked()
    {
        string savedId = controller.SaveBlueprint();
        _statusMessage = $"Saved as {savedId}";
    }

    public void OnLoadClicked()
    {
        _showLoadList = !_showLoadList;
        _availableIds = controller.GetAvailableBlueprintIds();
    }

    public void OnLoadCancelClicked() => _showLoadList = false;

    private void RefreshLoadList()
    {
        int count = Mathf.Min(_availableIds.Length, loadListRows.Length);
        for (int i = 0; i < loadListRows.Length; i++)
        {
            bool active = i < count;
            loadListRows[i].SetActive(active);
            if (active) loadListLabels[i].text = _availableIds[i];
        }
    }

    public void OnLoadListRowClicked(int index)
    {
        if (index >= _availableIds.Length) return;
        string id = _availableIds[index];
        controller.LoadBlueprint(id);
        _statusMessage = $"Loaded {id}";
        _showLoadList = false;
    }

    public void OnLoadListRow0() => OnLoadListRowClicked(0);
    public void OnLoadListRow1() => OnLoadListRowClicked(1);
    public void OnLoadListRow2() => OnLoadListRowClicked(2);
    public void OnLoadListRow3() => OnLoadListRowClicked(3);
    public void OnLoadListRow4() => OnLoadListRowClicked(4);
    public void OnLoadListRow5() => OnLoadListRowClicked(5);
    public void OnLoadListRow6() => OnLoadListRowClicked(6);
    public void OnLoadListRow7() => OnLoadListRowClicked(7);
    public void OnLoadListRow8() => OnLoadListRowClicked(8);
    public void OnLoadListRow9() => OnLoadListRowClicked(9);
    public void OnLoadListRow10() => OnLoadListRowClicked(10);
    public void OnLoadListRow11() => OnLoadListRowClicked(11);

    // -------------------------------------------------------------------------
    // Shared
    // -------------------------------------------------------------------------

    private static T CycleEnum<T>(T current, int direction) where T : Enum
    {
        var values = (T[])Enum.GetValues(typeof(T));
        int idx = Array.IndexOf(values, current);
        idx = (idx + direction + values.Length) % values.Length;
        return values[idx];
    }
}
