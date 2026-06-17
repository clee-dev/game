using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Add to the player prefab.
/// Tracks whether this player has physically spawned into the hub.
///
/// Hold E (keyboard) for 0.8s to toggle in or out.
/// When not spawned in: player is offscreen, invisible, no input.
/// When spawned in: player teleports to a hub spawn point and can move freely.
///
/// All connected players start unspawned. The mini-game starts only once
/// all spawned-in players enter the starting area.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class HubPlayerState : NetworkBehaviour
{
    /// <summary>All active HubPlayerState instances in the current scene.</summary>
    public static readonly List<HubPlayerState> All = new();

    [Header("References -- assign in Inspector")]
    [SerializeField] private GameObject      visuals;           // Character mesh root to show/hide
    [SerializeField] private CharacterController characterController;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerCamera     playerCamera;
    [SerializeField] private Camera           camera;

    [Header("Settings")]
    [SerializeField] private float   holdDuration     = 0.8f;
    [SerializeField] private Vector3 offscreenPosition = new(0, -200, 0);

    private readonly NetworkVariable<bool> _isSpawnedIn = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsSpawnedIn => _isSpawnedIn.Value;

    private float _holdTimer;
    private bool  _toggled;  // prevents double-toggle on a single hold
    private bool  _locked;   // set true when mini-game starts

    // -------------------------------------------------------------------------
    // Network lifecycle
    // -------------------------------------------------------------------------

    public override void OnNetworkSpawn()
    {
        All.Add(this);
        _isSpawnedIn.OnValueChanged += (_, val) => ApplySpawnState(val);

        // Start offscreen on all clients
        transform.position = offscreenPosition;
        ApplySpawnState(false);
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        _isSpawnedIn.OnValueChanged -= (_, val) => ApplySpawnState(val);
    }

    // -------------------------------------------------------------------------
    // Input (owner only)
    // -------------------------------------------------------------------------

    private void Update()
    {
        if (!IsOwner || _locked) return;

        bool holding = Keyboard.current?.eKey.isPressed ?? false;

        if (holding)
        {
            _holdTimer += Time.deltaTime;

            if (_holdTimer >= holdDuration && !_toggled)
            {
                _toggled = true;
                ToggleSpawnRpc();
            }
        }
        else
        {
            _holdTimer = 0f;
            _toggled   = false;
        }
    }

    // -------------------------------------------------------------------------
    // RPCs
    // -------------------------------------------------------------------------

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ToggleSpawnRpc()
    {
        if (_locked) return;

        bool spawning = !_isSpawnedIn.Value;
        _isSpawnedIn.Value = spawning;

        if (spawning)
        {
            Vector3 spawnPos = HubSpawnPoints.Instance != null
                ? HubSpawnPoints.Instance.GetSpawnPoint()
                : Vector3.zero;

            TeleportToRpc(spawnPos);
        }
        else
        {
            TeleportToRpc(offscreenPosition);
        }
    }

    /// <summary>Server tells the owner where to move their character controller.</summary>
    [Rpc(SendTo.Owner)]
    private void TeleportToRpc(Vector3 position)
    {
        characterController.enabled = false;
        transform.position          = position;
        characterController.enabled = true;
    }

    // -------------------------------------------------------------------------
    // State application (runs on all clients when NetworkVariable changes)
    // -------------------------------------------------------------------------

    private void ApplySpawnState(bool spawned)
    {
        if (visuals       != null) visuals.SetActive(spawned);
        if (playerController != null) playerController.enabled = spawned && IsOwner;
        if (playerCamera     != null) playerCamera.enabled     = spawned && IsOwner;
        if (camera           != null) camera.enabled           = spawned && IsOwner;
    }

    // -------------------------------------------------------------------------
    // Lock (called when mini-game starts)
    // -------------------------------------------------------------------------

    public void Lock()
    {
        _locked = true;
    }
}
