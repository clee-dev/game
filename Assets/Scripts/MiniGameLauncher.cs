using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Place in the hub world. Players walk into the trigger zone and press E to ready up.
/// When enough players are ready, a countdown runs and the host loads the mini-game scene.
///
/// Setup on the GameObject:
///   - NetworkObject component
///   - Sphere Collider with Is Trigger = true (set radius to your desired interaction range)
///   - This script
///   - A child Canvas (World Space) with a TextMeshProUGUI for status display
///
/// The scene must be added to File > Build Settings before it can be loaded.
/// </summary>
public class MiniGameLauncher : NetworkBehaviour
{
    [Header("Mini-Game Config")]
    [SerializeField] private string sceneName       = "MiniGame_Example";
    [SerializeField] private string displayName     = "Duck Race";
    [SerializeField] private int    requiredPlayers = 2;
    [SerializeField] private float  countdownDuration = 3f;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI statusText;   // World-space text above the launcher

    // Networked state -- all clients read these for display
    private readonly NetworkVariable<int>   _readyCount = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> _countdown  = new(-1f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Server-side only -- tracks which clients are ready
    private readonly HashSet<ulong> _readyPlayers = new();

    // Local state
    private bool        _localPlayerInRange;
    private bool        _localPlayerReady;
    private InputReader _localInputReader;
    private Coroutine   _countdownCoroutine;

    // -------------------------------------------------------------------------
    // Network lifecycle
    // -------------------------------------------------------------------------

    public override void OnNetworkSpawn()
    {
        _readyCount.OnValueChanged += (_, _) => UpdateStatusText();
        _countdown.OnValueChanged  += (_, _) => UpdateStatusText();
        UpdateStatusText();
    }

    // -------------------------------------------------------------------------
    // Update -- only runs interaction logic for local player when in range
    // -------------------------------------------------------------------------

    private void Update()
    {
        UpdateStatusText();

        if (!_localPlayerInRange || _localInputReader == null) return;

        if (_localInputReader.InteractPressed)
        {
            _localInputReader.ConsumeInteract();

            // Toggle this player's ready state
            _localPlayerReady = !_localPlayerReady;
            SetReadyServerRpc(NetworkManager.Singleton.LocalClientId, _localPlayerReady);
        }
    }

    // -------------------------------------------------------------------------
    // Trigger detection -- fires locally on each client
    // -------------------------------------------------------------------------

    private void OnTriggerEnter(Collider other)
    {
        // Debug.Log("Entered");
        // Only care about the LOCAL player entering the zone
        var netPlayer = other.GetComponent<NetworkPlayer>();
        if (netPlayer == null || !netPlayer.IsOwner) return;

        _localPlayerInRange  = true;
        _localInputReader    = other.GetComponent<InputReader>();
    }

    private void OnTriggerExit(Collider other)
    {
        // Debug.Log("exit");

        var netPlayer = other.GetComponent<NetworkPlayer>();
        if (netPlayer == null || !netPlayer.IsOwner) return;

        _localPlayerInRange = false;
        _localInputReader   = null;

        // Auto un-ready when leaving the zone
        if (_localPlayerReady)
        {
            _localPlayerReady = false;
            SetReadyServerRpc(NetworkManager.Singleton.LocalClientId, false);
        }
    }

    // -------------------------------------------------------------------------
    // Server RPC -- host validates and updates state
    // -------------------------------------------------------------------------

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SetReadyServerRpc(ulong clientId, bool ready)
    {
        if (ready)
            _readyPlayers.Add(clientId);
        else
            _readyPlayers.Remove(clientId);

        _readyCount.Value = _readyPlayers.Count;

        if (_readyCount.Value >= requiredPlayers)
        {
            // Enough players -- start countdown if not already running
            if (_countdownCoroutine == null)
                _countdownCoroutine = StartCoroutine(CountdownCoroutine());
        }
        else
        {
            // Not enough players -- cancel countdown
            if (_countdownCoroutine != null)
            {
                StopCoroutine(_countdownCoroutine);
                _countdownCoroutine = null;
                _countdown.Value    = -1f;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Countdown (runs on host only)
    // -------------------------------------------------------------------------

    private IEnumerator CountdownCoroutine()
    {
        float remaining = countdownDuration;

        while (remaining > 0f)
        {
            _countdown.Value = remaining;
            remaining       -= Time.deltaTime;
            yield return null;
        }

        _countdown.Value    = 0f;
        _countdownCoroutine = null;

        LoadScene();
    }

    private void LoadScene()
    {
        if (!IsServer) return;

        Debug.Log($"[MiniGameLauncher] Loading scene: {sceneName}");

        NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

    // -------------------------------------------------------------------------
    // Status display -- updates every frame on all clients
    // -------------------------------------------------------------------------

    private void UpdateStatusText()
    {
        if (statusText == null) return;

        if (_countdown.Value > 0f)
        {
            statusText.text = $"{displayName}\nStarting in {_countdown.Value:F1}s...";
            return;
        }

        string prompt = _localPlayerInRange
            ? (_localPlayerReady ? "[E] Cancel Ready" : "[E] Ready Up")
            : "";

        statusText.text = $"{displayName}\n{_readyCount.Value}/{requiredPlayers} Ready\n{prompt}";
    }
}
