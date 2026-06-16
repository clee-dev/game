using System;
using System.Linq;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles Steam lobby creation, code-based joining, and Steam friend invites.
///
/// Flow for hosting:
///   CreateLobby() → Steam lobby created → room code generated → NGO starts as Host
///
/// Flow for joining by code:
///   JoinByCode(code) → searches public lobbies with matching code → joins → NGO starts as Client
///
/// Flow for Steam friend invite (requires real App ID, not 480):
///   Friend clicks "Join Game" in Steam overlay → OnGameLobbyJoinRequested fires → JoinByLobbyId()
/// </summary>
public class SteamLobbyManager : MonoBehaviour
{
    public static SteamLobbyManager Instance { get; private set; }

    // Events -- LobbyUI subscribes to these
    public static event Action<string> OnLobbyCreated;       // passes the room code
    public static event Action         OnLobbyJoined;
    public static event Action<string> OnJoinFailed;          // passes error message
    public static event Action<string> OnPlayerJoined;        // passes player name
    public static event Action<string> OnPlayerLeft;          // passes player name

    private const int    MAX_PLAYERS = 4;
    private const string CODE_KEY    = "room_code";
    private const string HOST_ID_KEY = "host_steam_id";

    // Unambiguous characters -- removed 0/O and 1/I to avoid confusion
    private const string CODE_CHARS  = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int    CODE_LENGTH = 6;

    private Lobby _currentLobby;

    /// <summary>Room code for the current lobby. Used by VoiceManager to name the Vivox channel.</summary>
    public string CurrentRoomCode => _currentLobby.Id.IsValid ? _currentLobby.GetData(CODE_KEY) : string.Empty;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        SteamMatchmaking.OnLobbyMemberJoined    += OnMemberJoined;
        SteamMatchmaking.OnLobbyMemberLeave     += OnMemberLeft;
        SteamFriends.OnGameLobbyJoinRequested   += OnGameLobbyJoinRequested;
    }

    private void OnDisable()
    {
        SteamMatchmaking.OnLobbyMemberJoined    -= OnMemberJoined;
        SteamMatchmaking.OnLobbyMemberLeave     -= OnMemberLeft;
        SteamFriends.OnGameLobbyJoinRequested   -= OnGameLobbyJoinRequested;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public async void CreateLobby()
    {
        if (!SteamManager.Initialized)
        {
            OnJoinFailed?.Invoke("Steam is not running.");
            return;
        }

        try
        {
            var result = await SteamMatchmaking.CreateLobbyAsync(MAX_PLAYERS);

            if (!result.HasValue)
            {
                OnJoinFailed?.Invoke("Failed to create lobby.");
                return;
            }

            _currentLobby = result.Value;
            _currentLobby.SetPublic();
            _currentLobby.SetJoinable(true);

            string code = GenerateCode();
            _currentLobby.SetData(CODE_KEY,    code);
            _currentLobby.SetData(HOST_ID_KEY, SteamClient.SteamId.Value.ToString());

            NetworkManager.Singleton.StartHost();
            OnLobbyCreated?.Invoke(code);

            Debug.Log($"Lobby created. Code: {code}");
        }
        catch (Exception e)
        {
            OnJoinFailed?.Invoke($"Error creating lobby: {e.Message}");
            Debug.LogError(e);
        }
    }

    public async void JoinByCode(string code)
    {
        if (!SteamManager.Initialized)
        {
            OnJoinFailed?.Invoke("Steam is not running.");
            return;
        }

        code = code.Trim().ToUpper();

        if (code.Length != CODE_LENGTH)
        {
            OnJoinFailed?.Invoke($"Code must be {CODE_LENGTH} characters.");
            return;
        }

        try
        {
            var lobbies = await SteamMatchmaking.LobbyList
                .WithMaxResults(10)
                .WithKeyValue(CODE_KEY, code)
                .RequestAsync();

            if (lobbies == null || lobbies.Length == 0)
            {
                OnJoinFailed?.Invoke("No lobby found with that code. Check the code and try again.");
                return;
            }

            await JoinLobby(lobbies[0]);
        }
        catch (Exception e)
        {
            OnJoinFailed?.Invoke($"Error searching for lobby: {e.Message}");
            Debug.LogError(e);
        }
    }

    public void LeaveLobby()
    {
        _currentLobby.Leave();
        if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsClient)
            NetworkManager.Singleton.Shutdown();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async System.Threading.Tasks.Task JoinLobby(Lobby lobby)
    {
        var result = await lobby.Join();

        if (result != RoomEnter.Success)
        {
            OnJoinFailed?.Invoke($"Could not enter lobby ({result}).");
            return;
        }

        _currentLobby = lobby;

        string hostIdStr = lobby.GetData(HOST_ID_KEY);
        if (!ulong.TryParse(hostIdStr, out ulong hostId))
        {
            OnJoinFailed?.Invoke("Could not read host ID from lobby.");
            return;
        }

        // Point the Facepunch transport at the host and connect
        var transport = NetworkManager.Singleton.GetComponent<FacepunchTransport>();
        if (transport == null)
        {
            OnJoinFailed?.Invoke("FacepunchTransport component not found on NetworkManager.");
            return;
        }

        transport.targetSteamId = hostId;
        NetworkManager.Singleton.StartClient();

        OnLobbyJoined?.Invoke();
        Debug.Log($"Joined lobby hosted by Steam ID: {hostId}");
    }

    private string GenerateCode()
    {
        return new string(
            Enumerable.Range(0, CODE_LENGTH)
                      .Select(_ => CODE_CHARS[UnityEngine.Random.Range(0, CODE_CHARS.Length)])
                      .ToArray()
        );
    }

    // -------------------------------------------------------------------------
    // Steam callbacks
    // -------------------------------------------------------------------------

    private void OnMemberJoined(Lobby lobby, Friend friend)
    {
        Debug.Log($"{friend.Name} joined the lobby.");
        OnPlayerJoined?.Invoke(friend.Name);
    }

    private void OnMemberLeft(Lobby lobby, Friend friend)
    {
        Debug.Log($"{friend.Name} left the lobby.");
        OnPlayerLeft?.Invoke(friend.Name);
    }

    // Fires when a friend clicks "Join Game" from the Steam overlay.
    // NOTE: This requires a real Steam App ID to work. App ID 480 will not route these correctly.
    private async void OnGameLobbyJoinRequested(Lobby lobby, SteamId steamId)
    {
        Debug.Log($"Steam invite received. Joining lobby from {steamId}...");
        await JoinLobby(lobby);
    }
}