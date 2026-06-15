using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Tracks and broadcasts the game start signal over the network.
/// Place a GameObject in your scene with NetworkObject + this component.
/// The host calls StartGame() which propagates to all clients via NetworkVariable.
/// </summary>
public class ReadyManager : NetworkBehaviour
{
    public static ReadyManager Instance { get; private set; }

    private readonly NetworkVariable<bool> _gameStarted = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        _gameStarted.OnValueChanged += OnGameStartedChanged;

        // Handle case where we join after game already started
        if (_gameStarted.Value)
            GameEvents.FireGameStarted();
    }

    public override void OnNetworkDespawn()
    {
        _gameStarted.OnValueChanged -= OnGameStartedChanged;
    }

    private void OnGameStartedChanged(bool _, bool current)
    {
        if (current) GameEvents.FireGameStarted();
    }

    /// <summary>Host only. Broadcasts game start to all clients.</summary>
    public void StartGame()
    {
        if (!IsServer) return;
        _gameStarted.Value = true;
    }

    /// <summary>Host only. Resets so players can return to lobby.</summary>
    public void ResetGame()
    {
        if (!IsServer) return;
        _gameStarted.Value = false;
    }
}
