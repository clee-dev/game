using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Hub-only fixed-position terminal. Players raycast-target this via PlayerInteraction
/// (same pattern as LevelSelectKiosk/OrderStation), press E, and the whole connected
/// party is sent into the LevelEditor scene -- same NGO scene-transition mechanism
/// StartingAreaTrigger uses for Hub -> Game1. This game has no "personal/solo scene"
/// concept, so there's no way for just one player to slip into the editor alone; everyone
/// goes together and NetworkPlayer disables each player's gameplay components while the
/// LevelEditor scene is active (see NetworkPlayer.passiveScenes).
///
/// Setup: NetworkObject (placed directly in Hub.unity like StartingAreaTrigger), a
/// Collider for PlayerInteraction's raycast to hit, this script attached.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class LevelEditorAccessPoint : NetworkBehaviour
{
    private const string LevelEditorSceneName = "LevelEditor";

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void EnterLevelEditorRpc()
    {
        NetworkManager.Singleton.SceneManager.LoadScene(LevelEditorSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
    }
}
