using System;
using Steamworks;
using UnityEngine;

/// <summary>
/// Initializes Facepunch Steamworks and keeps it alive across scenes.
/// Must be in the scene before any other Steam calls are made.
/// Requires steam_appid.txt in the project root containing your App ID (use 480 for testing).
/// </summary>
public class SteamManager : MonoBehaviour
{
    public static SteamManager Instance { get; private set; }
    public static bool Initialized    { get; private set; }

    [Tooltip("Your Steam App ID. 480 = Spacewar test app. Replace when you have a real App ID.")]
    [SerializeField] private uint appId = 480;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        try
        {
            SteamClient.Init(appId, false);
            Initialized = true;
            Debug.Log($"Steam initialized. Logged in as: {SteamClient.Name} ({SteamClient.SteamId})");
        }
        catch (Exception e)
        {
            Initialized = false;
            Debug.LogError($"Steam failed to initialize: {e.Message}\n" +
                           "Make sure Steam is running and steam_appid.txt exists in your project root.");
        }
    }

    private void Update()
    {
        if (Initialized)
            SteamClient.RunCallbacks();
    }

    private void OnDestroy()
    {
        if (Initialized)
        {
            SteamClient.Shutdown();
            Initialized = false;
        }
    }

    private void OnApplicationQuit()
    {
        if (Initialized)
        {
            SteamClient.Shutdown();
            Initialized = false;
        }
    }
}
