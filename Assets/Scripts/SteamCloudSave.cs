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
}
