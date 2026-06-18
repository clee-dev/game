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
/// Setup: NetworkObject (registered in the NetworkManager's prefab list, or placed
/// directly in Hub.unity like StartingAreaTrigger), a Collider for PlayerInteraction's
/// raycast to hit, this script attached.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class LevelSelectKiosk : NetworkBehaviour
{
    private string[] _availableBlueprintIds = new string[0];
    private readonly NetworkVariable<FixedString64Bytes> _selectedBlueprintId = new(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public int OptionCount => _availableBlueprintIds.Length;
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

    public string DescribeOption(int index) => BlueprintLoader.Load(_availableBlueprintIds[index])?.name ?? _availableBlueprintIds[index];

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

    private void OnSelectedBlueprintChanged(FixedString64Bytes previous, FixedString64Bytes current)
    {
        if (GameSession.Instance != null)
            GameSession.Instance.SetSelectedBlueprint(current.ToString());
    }
}
