using System.Collections.Generic;
using Steamworks;
using UnityEngine;

public static class SteamCloudSave
{
    public static bool Write(string fileName, string content)
    {
        try
        {
            SteamRemoteStorage.FileWrite(fileName, System.Text.Encoding.UTF8.GetBytes(content));
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SteamCloudSave] Write failed: {e.Message}");
            return false;
        }
    }

    public static string Read(string fileName)
    {
        try
        {
            if (!SteamRemoteStorage.FileExists(fileName))
                return null;

            byte[] data = SteamRemoteStorage.FileRead(fileName);
            return System.Text.Encoding.UTF8.GetString(data);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SteamCloudSave] Read failed: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Lists Steam Cloud filenames matching prefix/suffix (e.g. "blueprint_", ".json").
    /// Used by BlueprintLoader to find user-saved blueprints alongside the built-in
    /// StreamingAssets ones. Returns an empty list if Steam isn't initialized.
    /// </summary>
    public static List<string> ListFiles(string prefix, string suffix)
    {
        var results = new List<string>();

        try
        {
            foreach (string fileName in SteamRemoteStorage.Files)
            {
                if (fileName.StartsWith(prefix) && fileName.EndsWith(suffix))
                    results.Add(fileName);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SteamCloudSave] ListFiles failed: {e.Message}");
        }

        return results;
    }
}
