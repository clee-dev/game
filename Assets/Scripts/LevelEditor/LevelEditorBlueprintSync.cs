using Newtonsoft.Json;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Mirrors the host's live LevelEditorController.Blueprint onto every other connected
/// client (Cameron: "only the host can make edits, other players just get the camera").
/// LevelEditorController is a plain, per-client-local MonoBehaviour with no networking
/// of its own -- this is the one networked object in the scene that bridges host edits
/// to everyone else's local copy, so their camera actually looks at something live.
///
/// Setup: place a single GameObject with a NetworkObject and this component in
/// LevelEditor.unity (in-scene placed, not a runtime-spawned prefab -- same pattern as
/// LevelSelectKiosk/LevelEditorAccessPoint in Hub.unity).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class LevelEditorBlueprintSync : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (IsServer)
            LevelEditorController.Instance.BlueprintChanged += BroadcastCurrentBlueprint;
        else
            RequestBlueprintRpc();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
            LevelEditorController.Instance.BlueprintChanged -= BroadcastCurrentBlueprint;
    }

    private void BroadcastCurrentBlueprint()
    {
        string json = JsonConvert.SerializeObject(LevelEditorController.Instance.Blueprint.ToBlueprintData());
        ApplyBlueprintRpc(json);
    }

    /// <summary>Non-host clients call this on spawn so they're caught up immediately --
    /// covers both the normal case (whole party loads the scene together) and a client
    /// joining the Steam lobby while the host is already mid-session, since NGO syncs
    /// late joiners into the host's current active scene automatically.</summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestBlueprintRpc() => BroadcastCurrentBlueprint();

    [Rpc(SendTo.NotServer)]
    private void ApplyBlueprintRpc(string json)
    {
        BlueprintData data = JsonConvert.DeserializeObject<BlueprintData>(json);
        LevelEditorController.Instance.ApplyRemoteBlueprint(data);
    }
}
