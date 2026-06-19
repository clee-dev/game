using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-only. Spawns one material at this position and replaces it after a
/// cooldown once it's picked up (Section 5.2: "Materials respawn after a cooldown
/// once picked up"). Not a NetworkObject itself -- only the materials it spawns
/// need to replicate. BuildSystem instantiates one of these per blueprint supply
/// zone, server-side only, so every instance can assume server context.
///
/// Setup: assign materialPrefab in the Inspector (a MaterialItem prefab with
/// NetworkObject + ClientNetworkTransform + PhysicsPickup + Rigidbody + Collider).
/// </summary>
public class SupplyZoneSpawner : MonoBehaviour
{
    [SerializeField] private MaterialItem materialPrefab;
    [SerializeField] private float respawnCooldown = 5f;

    public MaterialType MaterialType => materialPrefab.Type;

    // All live supply zones, server-side only -- lets OrderStation find "the supply
    // zone for this material type" without any blueprint-level link between the two.
    public static readonly List<SupplyZoneSpawner> All = new();

    private MaterialItem _current;

    private void OnEnable() => All.Add(this);
    private void OnDisable() => All.Remove(this);

    private void Start() => SpawnOne();

    private void Update()
    {
        if (_current == null) return;

        if (_current.State != MaterialState.Loose)
        {
            _current = null;
            Invoke(nameof(SpawnOne), respawnCooldown);
        }
    }

    private void SpawnOne()
    {
        var instance = Instantiate(materialPrefab, transform.position, Quaternion.identity);
        var netObj = instance.GetComponent<NetworkObject>();
        netObj.Spawn();
        netObj.DestroyWithScene = true;
        _current = instance;
    }
}
