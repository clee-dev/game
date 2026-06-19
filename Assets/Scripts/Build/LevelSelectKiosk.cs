using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Hub-only fixed-position kiosk. Players raycast-target this via PlayerInteraction
/// (same pattern as OrderStation), press E to open a pop-up listing every available
/// blueprint (built-in StreamingAssets ones plus anything saved from the Level Editor to
/// Steam Cloud), then press a number key to select one. The selection is broadcast to
/// every connected client so everyone's GameSession agrees on which blueprint loads when
/// the host triggers StartingAreaTrigger's scene change -- BuildSystem reads
/// GameSession.Instance.SelectedBlueprintId once Game1 loads.
///
/// The menu's last option is always "Enter Level Editor" (same scene-load RPC
/// LevelEditorAccessPoint uses) -- this is a second, more discoverable entry point
/// alongside the standalone LevelEditorAccessPoint terminal elsewhere in the Hub, not a
/// replacement for it.
///
/// Setup: NetworkObject (registered in the NetworkManager's prefab list, or placed
/// directly in Hub.unity like StartingAreaTrigger), a Collider for PlayerInteraction's
/// raycast to hit, this script attached.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class LevelSelectKiosk : NetworkBehaviour
{
    private const string LevelEditorSceneName = "LevelEditor";

    private string[] _availableBlueprintIds = new string[0];
    private readonly NetworkVariable<FixedString64Bytes> _selectedBlueprintId = new(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Blueprint options plus one trailing "Enter Level Editor" option.</summary>
    public int OptionCount => _availableBlueprintIds.Length + 1;
    public string SelectedBlueprintId => _selectedBlueprintId.Value.ToString();

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
    }

    /// <summary>Re-scans local + cloud blueprint ids. Called on spawn and whenever the menu opens.</summary>
    public void RefreshAvailableBlueprints()
    {
        _availableBlueprintIds = BlueprintLoader.GetAllBlueprintIds();
    }

    public string DescribeOption(int index) =>
        IsLevelEditorOption(index) ? "Enter Level Editor" : BlueprintLoader.Load(_availableBlueprintIds[index])?.name ?? _availableBlueprintIds[index];

    /// <summary>True for the trailing "Enter Level Editor" row, appended after every
    /// scanned blueprint id.</summary>
    public bool IsLevelEditorOption(int index) => index == _availableBlueprintIds.Length;

    /// <summary>Resolves a menu row to its blueprint id using this client's own scanned
    /// list. PlayerInteraction sends the resulting id (not the row index) over the RPC --
    /// the server's _availableBlueprintIds can legitimately be ordered differently (Steam
    /// Cloud file enumeration order isn't guaranteed to match across machines), so an index
    /// picked from one machine's list could resolve to the wrong blueprint on another.</summary>
    public string IdAt(int index) => _availableBlueprintIds[index];

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SelectBlueprintRpc(FixedString64Bytes blueprintId)
    {
        _selectedBlueprintId.Value = blueprintId;
    }

    /// <summary>Same scene-load RPC LevelEditorAccessPoint.EnterLevelEditorRpc() uses --
    /// duplicated rather than shared since each Hub terminal is meant to stay
    /// self-contained (see LevelEditorAccessPoint's own doc comment).</summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void EnterLevelEditorRpc()
    {
        NetworkManager.Singleton.SceneManager.LoadScene(LevelEditorSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    private void OnSelectedBlueprintChanged(FixedString64Bytes previous, FixedString64Bytes current)
    {
        if (GameSession.Instance != null)
            GameSession.Instance.SetSelectedBlueprint(current.ToString());
    }
}
