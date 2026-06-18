using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-only. Spawns one tool at this position and replaces it once it's picked up,
/// so the depot is never permanently emptied out -- unlike materials, tools are never
/// consumed by a build, but a player can still walk off with the only one, so the depot
/// mirrors SupplyZoneSpawner's "replace once gone" behavior. Not a NetworkObject itself
/// -- only the tool it spawns needs to replicate. BuildSystem instantiates one of these
/// per blueprint tool depot, server-side only, so every instance can assume server context.
///
/// Setup: assign toolPrefab in the Inspector (a ToolItem prefab with NetworkObject +
/// ClientNetworkTransform + PhysicsPickup + Rigidbody + Collider).
/// </summary>
public class ToolDepotSpawner : MonoBehaviour
{
    [SerializeField] private ToolItem toolPrefab;
    [SerializeField] private float respawnCooldown = 5f;

    private ToolItem _current;
    private PhysicsPickup _currentPickup;

    private void Start() => SpawnOne();

    private void Update()
    {
        if (_current == null) return;

        if (_currentPickup.IsHeld)
        {
            _current = null;
            _currentPickup = null;
            Invoke(nameof(SpawnOne), respawnCooldown);
        }
    }

    private void SpawnOne()
    {
        var instance = Instantiate(toolPrefab, transform.position, Quaternion.identity);
        instance.GetComponent<NetworkObject>().Spawn();
        _current = instance;
        _currentPickup = instance.GetComponent<PhysicsPickup>();
    }
}
