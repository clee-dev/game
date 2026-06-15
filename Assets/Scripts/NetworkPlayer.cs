using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Runs once when this player object spawns on the network.
/// If this is NOT our player, disable everything that should only run locally:
/// input, movement, camera, and audio listener.
///
/// This is the IsOwner pattern -- the foundation of all per-player
/// local vs remote logic in NGO. Phase 3 will expand on this significantly.
/// </summary>
public class NetworkPlayer : NetworkBehaviour
{
    [Header("Local-Only Components")]
    [SerializeField] private PlayerController playerController;
    [SerializeField] private InputReader      inputReader;
    [SerializeField] private PlayerCamera     playerCamera;
    [SerializeField] private Camera           playerCam;
    [SerializeField] private AudioListener    audioListener;

    public override void OnNetworkSpawn()
    {
        bool local = IsOwner;

        if (playerController != null) playerController.enabled = local;
        if (inputReader      != null) inputReader.enabled      = local;
        if (playerCamera     != null) playerCamera.enabled     = local;
        if (playerCam        != null) playerCam.enabled        = local;
        if (audioListener    != null) audioListener.enabled    = local;
    }
}
