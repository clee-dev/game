using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Burnout meter for the Welding Torch (PLANNED_FEATURES.md, Steel Material): continuous
/// build use heats this up; once it hits maxHeat the torch locks out for cooldownDuration
/// before it can be used again. Heat drains back down whenever the torch isn't actively
/// welding (not just during cooldown), so short bursts of use recover between builds
/// instead of only resetting after a full lockout.
///
/// Sits alongside ToolItem + PhysicsPickup on the WeldingTorch prefab. BuildTile calls
/// TryHeat() once per build tick while this specific torch instance drives a Torch build
/// (see BuildTile.ContinueBuildRpc/BuildTickCoroutine), and blocks build progress for any
/// tick where it returns false.
/// </summary>
[RequireComponent(typeof(ToolItem), typeof(NetworkObject))]
public class WeldingTorchFuel : NetworkBehaviour
{
    [Tooltip("Seconds of continuous welding before the torch overheats.")]
    [SerializeField] private float maxHeat = 8f;

    [Tooltip("Heat/sec recovered while not actively welding.")]
    [SerializeField] private float drainRate = 1f;

    [Tooltip("Placeholder -- Cameron to tune. Seconds the torch stays locked out once overheated.")]
    [SerializeField] private float cooldownDuration = 4f;

    private readonly NetworkVariable<float> _heat = new(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _overheated = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public float HeatFraction => maxHeat <= 0f ? 0f : Mathf.Clamp01(_heat.Value / maxHeat);
    public bool IsOverheated => _overheated.Value;

    private float _cooldownEndTime;
    private float _lastUsedTime = float.NegativeInfinity;

    /// <summary>Server-only. Called once per build tick while this torch is actively
    /// welding (a stillness-paused tick should not call this -- see BuildTile). Returns
    /// false when overheated, telling the caller to withhold build progress for that tick.</summary>
    public bool TryHeat(float deltaTime)
    {
        if (!IsServer) return true;
        if (_overheated.Value) return false;

        _lastUsedTime = Time.time;
        _heat.Value = Mathf.Min(maxHeat, _heat.Value + deltaTime);

        if (_heat.Value >= maxHeat)
        {
            _overheated.Value = true;
            _cooldownEndTime = Time.time + cooldownDuration;
            return false;
        }

        return true;
    }

    private void Update()
    {
        if (!IsServer) return;

        if (_overheated.Value)
        {
            if (Time.time >= _cooldownEndTime)
            {
                _overheated.Value = false;
                _heat.Value = 0f;
            }
            return;
        }

        // Passive cool-down -- only when a build tick hasn't touched this torch this frame,
        // so TryHeat's own increment isn't immediately undone the same frame it's applied.
        bool usedThisFrame = Time.time - _lastUsedTime < 0.01f;
        if (!usedThisFrame && _heat.Value > 0f)
            _heat.Value = Mathf.Max(0f, _heat.Value - drainRate * Time.deltaTime);
    }
}
