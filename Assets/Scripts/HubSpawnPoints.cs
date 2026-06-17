using UnityEngine;

/// <summary>
/// Place in the hub scene. Holds the spawn point positions players
/// teleport to when they hold E to spawn in.
/// </summary>
public class HubSpawnPoints : MonoBehaviour
{
    public static HubSpawnPoints Instance { get; private set; }

    [SerializeField] private Transform[] spawnPoints;

    private int _nextIndex;

    private void Awake()
    {
        Instance = this;
    }

    public Vector3 GetSpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return Vector3.zero;

        Vector3 pos = spawnPoints[_nextIndex % spawnPoints.Length].position;
        _nextIndex++;
        return pos;
    }
}
