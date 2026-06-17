using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Place in the hub world as a trigger zone (Collider with Is Trigger = true).
/// When all spawned-in players are standing inside, a countdown begins.
/// If anyone leaves, the countdown resets.
/// On completion, locks all players and loads the mini-game scene.
///
/// Requires: NetworkObject component on this GameObject.
/// </summary>
public class StartingAreaTrigger : NetworkBehaviour
{
    [Header("Config")]
    [SerializeField] private string miniGameSceneName  = "MiniGame_Example";
    [SerializeField] private float  countdownDuration  = 3f;
    [SerializeField] private int    minimumPlayers     = 1;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI countdownText;

    private readonly NetworkVariable<float> _countdown = new(
        -1f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Server-side: which players are currently inside the zone
    private readonly HashSet<ulong> _playersInZone = new();

    private Coroutine _countdownCoroutine;

    public override void OnNetworkSpawn()
    {
        _countdown.OnValueChanged += (_, val) => UpdateDisplay(val);
        UpdateDisplay(_countdown.Value);
    }

    // -------------------------------------------------------------------------
    // Trigger detection (server only)
    // -------------------------------------------------------------------------

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        var player = other.GetComponent<HubPlayerState>();
        if (player == null || !player.IsSpawnedIn) return;

        _playersInZone.Add(player.OwnerClientId);
        EvaluateCondition();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsServer) return;

        var player = other.GetComponent<HubPlayerState>();
        if (player == null) return;

        _playersInZone.Remove(player.OwnerClientId);
        EvaluateCondition();
    }

    // -------------------------------------------------------------------------
    // Condition check
    // -------------------------------------------------------------------------

    private void EvaluateCondition()
    {
        int spawnedCount = 0;
        foreach (var p in HubPlayerState.All)
            if (p.IsSpawnedIn) spawnedCount++;

        bool allInside = spawnedCount >= minimumPlayers
                      && _playersInZone.Count >= spawnedCount;

        if (allInside)
        {
            if (_countdownCoroutine == null)
                _countdownCoroutine = StartCoroutine(CountdownCoroutine());
        }
        else
        {
            if (_countdownCoroutine != null)
            {
                StopCoroutine(_countdownCoroutine);
                _countdownCoroutine = null;
                _countdown.Value    = -1f;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Countdown (host only)
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

        NetworkManager.Singleton.SceneManager.LoadScene(miniGameSceneName, LoadSceneMode.Single);
    }

    // -------------------------------------------------------------------------
    // Display
    // -------------------------------------------------------------------------

    private void UpdateDisplay(float val)
    {
        if (countdownText == null) return;
        countdownText.text = val > 0f ? $"Starting in {val:F0}s..." : "";
    }
}
