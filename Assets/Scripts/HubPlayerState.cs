using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Add to the player prefab.
/// When a player connects to the hub, they are automatically spawned in at a spawn point.
/// Visuals, movement, and camera are enabled immediately for the local player.
/// GameEvents.FireGameStarted() is called so mouse look activates.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class HubPlayerState : NetworkBehaviour
{
    public static readonly List<HubPlayerState> All = new();

    [Header("References -- assign in Inspector")]
    [SerializeField] private GameObject          visuals;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private PlayerController    playerController;
    [SerializeField] private PlayerCamera        playerCamera;
    [SerializeField] private Camera              cam;

    [Header("Settings")]
    [SerializeField] private Vector3 offscreenPosition = new(0, -200, 0);

    private readonly NetworkVariable<bool> _isSpawnedIn = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsSpawnedIn => _isSpawnedIn.Value;

    public override void OnNetworkSpawn()
    {
        All.Add(this);
        _isSpawnedIn.OnValueChanged += (_, val) => ApplySpawnState(val);

        // Only move offscreen if not already spawned in.
        // Joining clients receive the current NetworkVariable value in the spawn message,
        // so _isSpawnedIn may already be true for existing players -- don't overwrite their position.
        if (!_isSpawnedIn.Value)
            transform.position = offscreenPosition;

        if (IsServer)
        {
            Vector3 spawnPos = HubSpawnPoints.Instance != null
                ? HubSpawnPoints.Instance.GetSpawnPoint()
                : Vector3.zero;

            TeleportToRpc(spawnPos);
            _isSpawnedIn.Value = true;
        }

        // Apply current state now -- OnValueChanged won't fire for values
        // received at join time since the value didn't change from the client's perspective.
        ApplySpawnState(_isSpawnedIn.Value);
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        _isSpawnedIn.OnValueChanged -= (_, val) => ApplySpawnState(val);
    }

    [Rpc(SendTo.Owner)]
    private void TeleportToRpc(Vector3 position)
    {
        characterController.enabled = false;
        transform.position          = position;
        characterController.enabled = true;
    }

    private void ApplySpawnState(bool spawned)
    {
        if (visuals          != null) visuals.SetActive(spawned);
        if (playerController != null) playerController.enabled = spawned && IsOwner;
        if (playerCamera     != null) playerCamera.enabled     = spawned && IsOwner;
        if (cam              != null) cam.enabled              = spawned && IsOwner;

        if (spawned && IsOwner)
            GameEvents.FireGameStarted();
    }
}