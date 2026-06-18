using UnityEngine;

/// <summary>
/// Persistent cross-scene singleton holding which blueprint the next level load should
/// use. Set by LevelSelectKiosk when a player selects a blueprint in the Hub; read by
/// BuildSystem when the gameplay scene loads. Plain singleton (not a NetworkObject) --
/// each machine resolves its own copy locally, kept in sync because LevelSelectKiosk
/// broadcasts the selection to everyone via RPC before anyone leaves the Hub.
///
/// Setup: add to the Managers GameObject in Boot.unity alongside SteamManager/SaveManager,
/// following the same DontDestroyOnLoad pattern.
/// </summary>
public class GameSession : MonoBehaviour
{
    public static GameSession Instance { get; private set; }

    public string SelectedBlueprintId { get; private set; }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void SetSelectedBlueprint(string blueprintId)
    {
        SelectedBlueprintId = blueprintId;
    }
}
