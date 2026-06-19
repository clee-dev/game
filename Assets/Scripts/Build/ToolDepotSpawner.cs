using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-only. Spawns one tool per configured slot at this position and replaces each
/// one independently once it's picked up, so the depot is never permanently emptied out --
/// unlike materials, tools are never consumed by a build, but a player can still walk off
/// with the only one, so each slot mirrors SupplyZoneSpawner's "replace once gone" behavior.
/// Not a NetworkObject itself -- only the tools it spawns need to replicate. BuildSystem
/// instantiates one of these per blueprint tool depot, server-side only, so every instance
/// can assume server context.
///
/// Setup: assign toolPrefabs in the Inspector -- one ToolItem prefab per ToolType this
/// depot might ever need to offer (each a NetworkObject + ClientNetworkTransform +
/// PhysicsPickup + Rigidbody + Collider). Which of those actually spawn is decided per
/// instance by Configure(), called by BuildSystem right after instantiation using the
/// blueprint's ToolDepotData.tools list. If Configure() is never called (e.g. a depot
/// placed directly in a scene with no blueprint behind it), every assigned prefab spawns.
/// </summary>
public class ToolDepotSpawner : MonoBehaviour
{
    [SerializeField] private ToolItem[] toolPrefabs;
    [SerializeField] private float respawnCooldown = 5f;

    private class Slot
    {
        public ToolItem Prefab;
        public ToolItem Current;
        public PhysicsPickup CurrentPickup;
    }

    private readonly List<Slot> _slots = new();

    /// <summary>Restricts this depot to the given tool types (blueprint's ToolDepotData.tools).</summary>
    public void Configure(string[] toolTypeNames)
    {
        _slots.Clear();

        foreach (string name in toolTypeNames)
        {
            ToolType type = BlueprintEnums.ParseToolType(name);
            ToolItem prefab = toolPrefabs.FirstOrDefault(p => p.Type == type);
            if (prefab == null)
            {
                Debug.LogWarning($"[ToolDepotSpawner] No prefab assigned for tool type '{name}'");
                continue;
            }

            _slots.Add(new Slot { Prefab = prefab });
        }
    }

    private void Start()
    {
        if (_slots.Count == 0)
            foreach (var prefab in toolPrefabs)
                _slots.Add(new Slot { Prefab = prefab });

        foreach (var slot in _slots)
            SpawnSlot(slot);
    }

    private void Update()
    {
        foreach (var slot in _slots)
        {
            if (slot.Current == null || !slot.CurrentPickup.IsHeld) continue;

            slot.Current = null;
            slot.CurrentPickup = null;
            StartCoroutine(RespawnSlotAfterDelay(slot));
        }
    }

    private IEnumerator RespawnSlotAfterDelay(Slot slot)
    {
        yield return new WaitForSeconds(respawnCooldown);
        SpawnSlot(slot);
    }

    private void SpawnSlot(Slot slot)
    {
        var instance = Instantiate(slot.Prefab, transform.position, Quaternion.identity);
        var netObj = instance.GetComponent<NetworkObject>();
        netObj.Spawn();
        netObj.DestroyWithScene = true;
        slot.Current = instance;
        slot.CurrentPickup = instance.GetComponent<PhysicsPickup>();
    }
}
