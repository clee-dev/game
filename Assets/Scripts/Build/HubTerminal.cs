using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Hub-only fixed-position terminal. A richer drop-in alternative to LevelSelectKiosk:
/// same NetworkVariable-synced selection (any connected player can confirm a pick, no
/// host-only gate -- PLANNED_FEATURES.md's Hub Terminal decisions) and the same trailing
/// "Enter Level Editor" option, but each row also surfaces tile count / required
/// materials / completion threshold, and this class exposes a procedurally-painted
/// top-down preview texture per blueprint. PlayerInteraction renders everything via
/// OnGUI (see DrawTerminalMenu) -- screen-space-overlay, matching every other in-game
/// menu, rather than a world-space Canvas: this codebase has no EventSystem /
/// PhysicsRaycaster plumbing for world-space UI raycasting anywhere yet, and adding that
/// machinery for one terminal would be a much bigger change than this feature calls for.
///
/// LevelSelectKiosk is left in place and untouched elsewhere in the Hub
/// (PLANNED_FEATURES.md: "keep the old script as a fallback").
///
/// Setup: NetworkObject (placed directly in Hub.unity, same as LevelSelectKiosk), a
/// Collider for PlayerInteraction's raycast to hit, this script attached.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class HubTerminal : NetworkBehaviour
{
    private const string LevelEditorSceneName = "LevelEditor";

    // Caps how large a single preview texture can get -- existing blueprints all stay
    // well under this, but a sprawling future one shouldn't allocate something huge.
    private const int PreviewMaxDimension = 48;

    private string[] _availableBlueprintIds = new string[0];
    private BlueprintData[] _cachedData = new BlueprintData[0];
    private readonly Dictionary<string, Texture2D> _previewCache = new();

    private readonly NetworkVariable<FixedString64Bytes> _selectedBlueprintId = new(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Blueprint options plus one trailing "Enter Level Editor" option.</summary>
    public int OptionCount => _availableBlueprintIds.Length + 1;
    public string SelectedBlueprintId => _selectedBlueprintId.Value.ToString();

    /// <summary>Fires whenever the synced selection changes, so PlayerInteraction can
    /// flash a brief lock-in indicator (PLANNED_FEATURES.md: "visual feedback when
    /// selection is confirmed").</summary>
    public event System.Action SelectionConfirmed;

    public override void OnNetworkSpawn()
    {
        RefreshAvailableBlueprints();
        _selectedBlueprintId.OnValueChanged += OnSelectedBlueprintChanged;

        // Covers players who join after a selection was already made -- OnValueChanged
        // only fires on a future change, not for the value this client syncs on spawn.
        if (!_selectedBlueprintId.Value.IsEmpty && GameSession.Instance != null)
            GameSession.Instance.SetSelectedBlueprint(_selectedBlueprintId.Value.ToString());
    }

    public override void OnNetworkDespawn()
    {
        _selectedBlueprintId.OnValueChanged -= OnSelectedBlueprintChanged;

        foreach (Texture2D tex in _previewCache.Values) Destroy(tex);
        _previewCache.Clear();
    }

    /// <summary>Re-scans local + cloud blueprint ids and reloads their data. Called on
    /// spawn and whenever the menu opens.</summary>
    public void RefreshAvailableBlueprints()
    {
        _availableBlueprintIds = BlueprintLoader.GetAllBlueprintIds();
        _cachedData = new BlueprintData[_availableBlueprintIds.Length];
        for (int i = 0; i < _availableBlueprintIds.Length; i++)
            _cachedData[i] = BlueprintLoader.Load(_availableBlueprintIds[i]);
    }

    /// <summary>True for the trailing "Enter Level Editor" row, appended after every
    /// scanned blueprint id.</summary>
    public bool IsLevelEditorOption(int index) => index == _availableBlueprintIds.Length;

    /// <summary>Resolves a menu row to its blueprint id using this client's own scanned
    /// list. See LevelSelectKiosk.IdAt for why the id (not the row index) is what
    /// actually gets sent over the RPC.</summary>
    public string IdAt(int index) => _availableBlueprintIds[index];

    public string DescribeOption(int index) =>
        IsLevelEditorOption(index) ? "Enter Level Editor" : (_cachedData[index]?.name ?? _availableBlueprintIds[index]);

    /// <summary>Tile count / required materials / completion threshold line shown under
    /// each row's name. Empty for the trailing "Enter Level Editor" option.</summary>
    public string DescribeDetails(int index)
    {
        if (IsLevelEditorOption(index)) return "";

        BlueprintData data = _cachedData[index];
        if (data == null) return "(failed to load)";

        int tileCount = data.tiles?.Length ?? 0;
        string materials = MaterialSummary(data);
        string threshold = data.contractDefaults?.completionThresholds != null
            ? $"{Mathf.RoundToInt(data.contractDefaults.completionThresholds.full * 100f)}% full"
            : "no contract";

        return $"{tileCount} tiles -- {materials} -- {threshold}";
    }

    private static string MaterialSummary(BlueprintData data)
    {
        if (data.tiles == null || data.tiles.Length == 0) return "no materials";

        var seen = new List<MaterialType>();
        foreach (TileData tile in data.tiles)
        {
            if (string.IsNullOrEmpty(tile.requiredMaterial)) continue;

            MaterialType material = BlueprintEnums.ParseMaterialType(tile.requiredMaterial);
            if (material != MaterialType.Any && !seen.Contains(material))
                seen.Add(material);
        }

        return seen.Count == 0 ? "any material" : string.Join("/", seen);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SelectBlueprintRpc(FixedString64Bytes blueprintId)
    {
        _selectedBlueprintId.Value = blueprintId;
    }

    /// <summary>Same scene-load RPC LevelEditorAccessPoint.EnterLevelEditorRpc() and
    /// LevelSelectKiosk.EnterLevelEditorRpc() use -- duplicated rather than shared since
    /// each Hub terminal is meant to stay self-contained (see LevelEditorAccessPoint's
    /// own doc comment).</summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void EnterLevelEditorRpc()
    {
        NetworkManager.Singleton.SceneManager.LoadScene(LevelEditorSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    private void OnSelectedBlueprintChanged(FixedString64Bytes previous, FixedString64Bytes current)
    {
        if (GameSession.Instance != null)
            GameSession.Instance.SetSelectedBlueprint(current.ToString());

        SelectionConfirmed?.Invoke();
    }

    // -------------------------------------------------------------------------
    // Top-down preview -- one tiny Texture2D per blueprint, painted once and
    // cached. Flattens to the topmost tile (highest position.y -- a vertical
    // layer index, not world height) per (x, z) column using the shared
    // TileTypeColors table, the same colors the Level Editor uses for its
    // placeholder tile cubes. Cropped to the blueprint's actual tile bounding box
    // rather than its nominal gridSize: existing blueprints all declare a 10x3x10
    // gridSize but only occupy a handful of tiles, so cropping to content avoids
    // a mostly-empty preview.
    // -------------------------------------------------------------------------

    public Texture2D GetPreviewTexture(int index)
    {
        if (IsLevelEditorOption(index)) return null;

        string id = _availableBlueprintIds[index];
        if (_previewCache.TryGetValue(id, out Texture2D cached)) return cached;

        Texture2D built = BuildPreviewTexture(_cachedData[index]);
        _previewCache[id] = built;
        return built;
    }

    private static Texture2D BuildPreviewTexture(BlueprintData data)
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };

        if (data?.tiles == null || data.tiles.Length == 0)
        {
            tex.SetPixel(0, 0, Color.clear);
            tex.Apply();
            return tex;
        }

        int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
        foreach (TileData tile in data.tiles)
        {
            minX = Mathf.Min(minX, tile.position.x);
            maxX = Mathf.Max(maxX, tile.position.x);
            minZ = Mathf.Min(minZ, tile.position.z);
            maxZ = Mathf.Max(maxZ, tile.position.z);
        }

        int width = Mathf.Clamp(maxX - minX + 1, 1, PreviewMaxDimension);
        int height = Mathf.Clamp(maxZ - minZ + 1, 1, PreviewMaxDimension);

        var topTileAt = new Dictionary<(int x, int z), TileData>();
        foreach (TileData tile in data.tiles)
        {
            var key = (tile.position.x, tile.position.z);
            if (!topTileAt.TryGetValue(key, out TileData existing) || tile.position.y > existing.position.y)
                topTileAt[key] = tile;
        }

        var pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

        foreach (var kvp in topTileAt)
        {
            int px = kvp.Key.x - minX;
            int pz = kvp.Key.z - minZ;
            if (px < 0 || px >= width || pz < 0 || pz >= height) continue; // outside the crop cap

            TileType type = BlueprintEnums.ParseTileType(kvp.Value.type);
            pixels[pz * width + px] = TileTypeColors.ColorFor(type);
        }

        tex.Reinitialize(width, height);
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}
