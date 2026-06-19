using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Per-level countdown (PLANNED_FEATURES.md, Phase B -- Timer System). Server ticks
/// _remainingTime down from the active blueprint's contractDefaults.timeLimitSeconds;
/// NetworkVariable replicates it to every client for display. Hitting zero forces
/// BuildSystem.EvaluateCompletion with whatever percentage was reached -- the same
/// evaluation natural 100% completion already goes through.
///
/// Setup: in-scene-placed NetworkObject in Game1.unity (same pattern as
/// LevelEditorBlueprintSync in LevelEditor.unity) -- no DefaultNetworkPrefabs entry
/// needed since it's never runtime-instantiated.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class LevelTimer : NetworkBehaviour
{
    public static LevelTimer Instance { get; private set; }

    private readonly NetworkVariable<float> _remainingTime = new(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public float RemainingTime => _remainingTime.Value;

    private void Awake() => Instance = this;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            _remainingTime.Value = BuildSystem.Instance?.CurrentBlueprint?.contractDefaults?.timeLimitSeconds ?? 0;

        _remainingTime.OnValueChanged += OnRemainingTimeChanged;
    }

    public override void OnNetworkDespawn() => _remainingTime.OnValueChanged -= OnRemainingTimeChanged;

    private void Update()
    {
        if (!IsServer || _remainingTime.Value <= 0f) return;
        _remainingTime.Value = Mathf.Max(0f, _remainingTime.Value - Time.deltaTime);
    }

    /// <summary>Fires identically on every machine the instant the replicated value
    /// crosses zero -- mirrors BuildTile's _state.OnValueChanged pattern for client-local
    /// reactions to server-authoritative state, so the level-end evaluation isn't
    /// server-only.</summary>
    private void OnRemainingTimeChanged(float previous, float now)
    {
        if (previous > 0f && now <= 0f)
            BuildSystem.Instance.EvaluateCompletion(forced: true);
    }
}
