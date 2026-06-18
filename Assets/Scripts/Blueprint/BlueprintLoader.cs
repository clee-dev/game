using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Reads blueprint JSON files from Application.streamingAssetsPath/Blueprints/ (built-in
/// blueprints shipped with the game) and from Steam Cloud (blueprints saved by players in
/// the Level Editor). Pure data loading -- does not touch any runtime/networked state.
/// BuildSystem calls this on level load and builds the live structures from the result.
/// </summary>
public static class BlueprintLoader
{
    private const string BlueprintFolder = "Blueprints";
    private const string CloudPrefix = "blueprint_";
    private const string CloudSuffix = ".json";

    public static BlueprintData Load(string blueprintId)
    {
        string path = LocalPath(blueprintId);
        if (File.Exists(path))
            return JsonConvert.DeserializeObject<BlueprintData>(File.ReadAllText(path));

        string cloudJson = SteamCloudSave.Read(blueprintId + CloudSuffix);
        if (cloudJson != null)
            return JsonConvert.DeserializeObject<BlueprintData>(cloudJson);

        Debug.LogError($"[BlueprintLoader] No blueprint found for id '{blueprintId}' (checked {path} and Steam Cloud)");
        return null;
    }

    /// <summary>
    /// Scans the local StreamingAssets folder and Steam Cloud for all available blueprint
    /// ids. Used by the Hub's LevelSelectKiosk and the Level Editor's load panel.
    /// </summary>
    public static string[] GetAllBlueprintIds()
    {
        var ids = new HashSet<string>();

        string dir = Path.Combine(Application.streamingAssetsPath, BlueprintFolder);
        if (Directory.Exists(dir))
            foreach (string file in Directory.GetFiles(dir, "*.json"))
                ids.Add(Path.GetFileNameWithoutExtension(file));

        foreach (string fileName in SteamCloudSave.ListFiles(CloudPrefix, CloudSuffix))
            ids.Add(Path.GetFileNameWithoutExtension(fileName));

        var result = new string[ids.Count];
        ids.CopyTo(result);
        return result;
    }

    /// <summary>
    /// Saves a blueprint to Steam Cloud so it can be selected later from the Hub. data.id
    /// must already carry the "blueprint_" prefix (LevelEditorController normalizes this
    /// before calling here) -- it doubles as the exact Steam Cloud filename, so there's no
    /// separate id-to-filename mapping to keep in sync.
    /// </summary>
    public static bool SaveToCloud(BlueprintData data)
    {
        string json = JsonConvert.SerializeObject(data, Formatting.Indented);
        return SteamCloudSave.Write(data.id + CloudSuffix, json);
    }

    private static string LocalPath(string blueprintId) =>
        Path.Combine(Application.streamingAssetsPath, BlueprintFolder, blueprintId + CloudSuffix);
}
