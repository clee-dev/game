using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Hub world HUD. Always visible while in the hub.
/// Shows: room code, who's connected and whether they've spawned in,
/// and the hold-E prompt for local player.
///
/// Attach to a Canvas in the hub scene and assign the text references.
/// </summary>
public class HubUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI roomCodeText;
    [SerializeField] private TextMeshProUGUI playerListText;
    [SerializeField] private TextMeshProUGUI holdPromptText;

    private float _refreshTimer;
    private const float REFRESH_INTERVAL = 0.5f;

    private void Start()
    {
        if (roomCodeText != null && SteamLobbyManager.Instance != null)
            roomCodeText.text = $"Room: {SteamLobbyManager.Instance.CurrentRoomCode}";

        if (holdPromptText != null)
            holdPromptText.text = "Hold E to spawn in";
    }

    private void Update()
    {
        _refreshTimer += Time.deltaTime;
        if (_refreshTimer < REFRESH_INTERVAL) return;
        _refreshTimer = 0f;
        RefreshPlayerList();
    }

    private void RefreshPlayerList()
    {
        if (playerListText == null) return;

        var sb = new StringBuilder();
        foreach (var player in HubPlayerState.All)
        {
            string status = player.IsSpawnedIn ? "ready" : "watching";
            sb.AppendLine($"Player {player.OwnerClientId} -- {status}");
        }

        playerListText.text = sb.ToString();
    }
}
