using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Reads blueprint JSON files from Application.streamingAssetsPath/Blueprints/.
/// Pure data loading -- does not touch any runtime/networked state.
/// BuildSystem calls this on level load and builds the live structures from the result.
/// </summary>
public static class BlueprintLoader
{
    private const string BlueprintFolder = "Blueprints";

    public static BlueprintData Load(string blueprintId)
    {
        string path = Path.Combine(Application.streamingAssetsPath, BlueprintFolder, blueprintId + ".json");

        if (!File.Exists(path))
        {
            Debug.LogError($"[BlueprintLoader] No blueprint found at {path}");
            return null;
        }

        string json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<BlueprintData>(json);
    }

    /// <summary>
    /// Scans the Blueprints folder for all .json files. Used by the contract board to
    /// populate available contracts -- not needed for the MVP single-blueprint flow.
    /// </summary>
    public static string[] GetAllBlueprintIds()
    {
        string dir = Path.Combine(Application.streamingAssetsPath, BlueprintFolder);
        if (!Directory.Exists(dir)) return new string[0];

        var files = Directory.GetFiles(dir, "*.json");
        for (int i = 0; i < files.Length; i++)
            files[i] = Path.GetFileNameWithoutExtension(files[i]);

        return files;
    }
}
