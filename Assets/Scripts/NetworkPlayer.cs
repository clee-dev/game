using Unity.Collections;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

/// <summary>
/// Runs once when this player object spawns on the network.
/// If this is NOT our player, disable everything that should only run locally:
/// input, movement, camera, and audio listener.
///
/// This is the IsOwner pattern -- the foundation of all per-player
/// local vs remote logic in NGO. Phase 3 will expand on this significantly.
///
/// Also re-evaluates on every scene change: the LevelEditor scene was built as a
/// single-user dev tool (its own orthographic LevelEditorCamera, no awareness of
/// Player avatars), but entering it from the Hub still carries every connected
/// player's NetworkObject along (same NGO scene-transition mechanism as Hub ->
/// Game1 -- this game has no "personal/solo scene" concept). So gameplay
/// components stay force-disabled for everyone, owner included, while
/// passiveScenes lists the active scene.
/// </summary>
public class NetworkPlayer : NetworkBehaviour
{
    [Header("Local-Only Components")]
    [SerializeField] private PlayerController  playerController;
    [SerializeField] private InputReader       inputReader;
    [SerializeField] private PlayerCamera      playerCamera;
    [SerializeField] private Camera            playerCam;
    [SerializeField] private AudioListener     audioListener;
    [SerializeField] private PlayerInteraction playerInteraction;

    [Tooltip("Scenes where the gameplay components above stay disabled even for the owner (e.g. LevelEditor, which has its own camera/controls).")]
    [SerializeField] private string[] passiveScenes = { "LevelEditor" };

    public override void OnNetworkSpawn()
    {
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        ApplyComponentState();
    }

    public override void OnNetworkDespawn()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    private void OnActiveSceneChanged(Scene previous, Scene current) => ApplyComponentState();

    private void ApplyComponentState()
    {
        bool local = IsOwner && !IsPassiveScene();

        if (playerController  != null) playerController.enabled  = local;
        if (inputReader       != null) inputReader.enabled       = local;
        if (playerCamera      != null) playerCamera.enabled      = local;
        if (playerCam         != null) playerCam.enabled         = local;
        if (audioListener     != null) audioListener.enabled     = local;
        if (playerInteraction != null) playerInteraction.enabled = local;
    }

    private bool IsPassiveScene()
    {
        string activeName = SceneManager.GetActiveScene().name;
        foreach (string sceneName in passiveScenes)
            if (sceneName == activeName) return true;
        return false;
    }

    /// <summary>
    /// Relays a scene-change request to the server. NGO's NetworkSceneManager.LoadScene
    /// can only be called server-side, so clients (e.g. a pause menu's "Leave to Hub"
    /// button, or the Hub's LevelEditorAccessPoint) route through the local player's own
    /// NetworkObject rather than calling it directly.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestLoadSceneRpc(FixedString64Bytes sceneName)
    {
        NetworkManager.Singleton.SceneManager.LoadScene(sceneName.ToString(), UnityEngine.SceneManagement.LoadSceneMode.Single);
    }
}
