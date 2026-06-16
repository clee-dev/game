using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Vivox;
using UnityEngine;

/// <summary>
/// Manages Vivox proximity voice chat.
///
/// Flow:
///   Game start → InitializeAsync (UGS + Vivox) → LoginAsync
///   Lobby created/joined → JoinChannelAsync (channel name = room code)
///   Every 0.3s in-game → Set3DPosition (using local player's camera)
///   Lobby left → LeaveChannelAsync → LogoutAsync
///
/// The channel name is the room code, so all players in the same lobby
/// join the same Vivox channel automatically.
///
/// Requires: Vivox package installed, Unity project linked to Unity Dashboard,
/// Vivox enabled in the Dashboard under Products > Communication & Safety.
/// </summary>
public class VoiceManager : MonoBehaviour
{
    public static VoiceManager Instance { get; private set; }
    private string _pendingChannel;

    [Header("Proximity Settings")]
    [Tooltip("Maximum distance at which players can hear each other (Unity units).")]
    [SerializeField] private float audibleDistance      = 25f;
    [Tooltip("Distance at which voice is at full volume.")]
    [SerializeField] private float conversationalDistance = 7f;
    [Tooltip("Muted by default when joining. Player can unmute themselves.")]
    [SerializeField] private bool  startMuted           = false;

    private string _activeChannel;
    private bool   _initialized;
    private bool   _loggedIn;
    private float  _posUpdateTimer;
    private bool   _isMuted;

    private const float POS_UPDATE_INTERVAL = 0.3f; // Vivox recommends ~3 updates per second

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private async void Start()
    {
        await InitializeAsync();
    }

    private void OnEnable()
    {
        SteamLobbyManager.OnLobbyCreated += OnLobbyCreated;
        SteamLobbyManager.OnLobbyJoined  += OnLobbyJoined;
    }

    private void OnDisable()
    {
        SteamLobbyManager.OnLobbyCreated -= OnLobbyCreated;
        SteamLobbyManager.OnLobbyJoined  -= OnLobbyJoined;
    }

    private void Update()
    {
        if (string.IsNullOrEmpty(_activeChannel)) return;
        if (Camera.main == null) return; // local player not spawned yet

        _posUpdateTimer += Time.deltaTime;
        if (_posUpdateTimer < POS_UPDATE_INTERVAL) return;
        _posUpdateTimer = 0f;

        // Pass the local player's camera GameObject.
        // Camera.main is only the local player's camera (all others are disabled by NetworkPlayer).
        VivoxService.Instance.Set3DPosition(Camera.main.gameObject, _activeChannel);
    }

    private void OnApplicationQuit()
    {
        if (_loggedIn)
            _ = LogoutAsync();
    }

    // -------------------------------------------------------------------------
    // Initialization
    // -------------------------------------------------------------------------

    private async Task InitializeAsync()
    {
        try
        {
            // Unity Gaming Services must be initialized before Vivox
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();

            // Anonymous authentication -- no account required from the player
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            await VivoxService.Instance.InitializeAsync();
            _initialized = true;
            Debug.Log("[VoiceManager] Vivox initialized.");

            await LoginAsync();
        }
        catch (Exception e)
        {
            // Voice chat is non-critical -- game is still playable without it
            Debug.LogWarning($"[VoiceManager] Vivox init failed (voice chat unavailable): {e.Message}");
        }
    }

    private async Task LoginAsync()
    {
        if (!_initialized) return;

        try
        {
            await VivoxService.Instance.LoginAsync();
            _loggedIn = true;
            _isMuted  = startMuted;

            if (_isMuted)
                VivoxService.Instance.MuteInputDevice();

            Debug.Log("[VoiceManager] Logged into Vivox.");

            if (!string.IsNullOrEmpty(_pendingChannel))
            {
                string channel = _pendingChannel;
                _pendingChannel = null;
                await JoinChannelAsync(channel);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VoiceManager] Vivox login failed: {e.Message}");
        }
    }

    private async Task LogoutAsync()
    {
        if (!_loggedIn) return;
        try
        {
            await VivoxService.Instance.LogoutAsync();
            _loggedIn = false;
            _activeChannel = null;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VoiceManager] Vivox logout error: {e.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Channel management
    // -------------------------------------------------------------------------

    private async void OnLobbyCreated(string roomCode)
    {
        await JoinChannelAsync(roomCode);
    }

    private async void OnLobbyJoined()
    {
        // Read the room code from the lobby metadata -- same code host used as channel name
        string roomCode = SteamLobbyManager.Instance.CurrentRoomCode;
        if (!string.IsNullOrEmpty(roomCode))
            await JoinChannelAsync(roomCode);
    }

    private async Task JoinChannelAsync(string channelName)
    {
        if (!_loggedIn)
        {
            _pendingChannel = channelName;
            Debug.Log($"[VoiceManager] Login in progress, will join '{channelName}' once ready.");
            return;
}

        // Leave any existing channel first
        if (!string.IsNullOrEmpty(_activeChannel))
            await LeaveChannelAsync();

        try
        {
            var properties = new Channel3DProperties(
                (int)audibleDistance,
                (int)conversationalDistance,
                1f,
                AudioFadeModel.InverseByDistance
            );

            await VivoxService.Instance.JoinPositionalChannelAsync(
                channelName,
                ChatCapability.AudioOnly,
                properties
            );

            _activeChannel = channelName;
            Debug.Log($"[VoiceManager] Joined positional channel: {channelName}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VoiceManager] Failed to join Vivox channel: {e.Message}");
        }
    }

    public async void LeaveChannel()
    {
        await LeaveChannelAsync();
    }

    private async Task LeaveChannelAsync()
    {
        if (string.IsNullOrEmpty(_activeChannel)) return;

        try
        {
            await VivoxService.Instance.LeaveChannelAsync(_activeChannel);
            _activeChannel = null;
            Debug.Log("[VoiceManager] Left Vivox channel.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VoiceManager] Error leaving Vivox channel: {e.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Mute toggle -- call this from your UI
    // -------------------------------------------------------------------------

    public void ToggleMute()
    {
        if (!_loggedIn) return;

        try
        {
            if (_isMuted)
            {
                VivoxService.Instance.UnmuteInputDevice();
                _isMuted = false;
            }
            else
            {
                VivoxService.Instance.MuteInputDevice();
                _isMuted = true;
            }
            Debug.Log($"[VoiceManager] Muted: {_isMuted}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VoiceManager] Mute toggle error: {e.Message}");
        }
    }

    public bool IsMuted => _isMuted;
}