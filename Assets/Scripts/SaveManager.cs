using System.IO;
using UnityEngine;

public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    public SaveData Data { get; private set; }

    private const string SaveFileName = "savedata.json";
    private string LocalFilePath => Path.Combine(Application.persistentDataPath, SaveFileName);

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        Load();
    }

    public void Load()
    {
        string json = null;

        // Try Steam Cloud first
        json = SteamCloudSave.Read(SaveFileName);

        if (json == null)
        {
            // Fall back to local file
            if (File.Exists(LocalFilePath))
            {
                json = File.ReadAllText(LocalFilePath);
                Debug.Log("[SaveManager] Loaded from local fallback.");
            }
        }
        else
        {
            Debug.Log("[SaveManager] Loaded from Steam Cloud.");
        }

        if (json != null)
        {
            Data = JsonUtility.FromJson<SaveData>(json);
        }
        else
        {
            Debug.Log("[SaveManager] No save found. Starting fresh.");
            Data = new SaveData();
        }
    }

    public void Save()
    {
        string json = JsonUtility.ToJson(Data, prettyPrint: true);

        // Write to Steam Cloud
        bool cloudSuccess = SteamCloudSave.Write(SaveFileName, json);
        if (!cloudSuccess)
            Debug.LogWarning("[SaveManager] Steam Cloud write failed. Local file still saved.");

        // Always write local fallback
        File.WriteAllText(LocalFilePath, json);
        Debug.Log("[SaveManager] Saved.");
    }

    public void ResetToDefault()
    {
        Data = new SaveData();
        Save();
        Debug.Log("[SaveManager] Reset to default.");
    }

    private void OnApplicationQuit()
    {
        Save();
    }
}
