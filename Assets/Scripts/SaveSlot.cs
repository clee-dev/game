using System;

[System.Serializable]
public class SaveSlot
{
    public string slotName     = "New Game";
    public string lastPlayed   = "";
    public int    gamesCompleted = 0;

    public void UpdateLastPlayed()
    {
        lastPlayed = DateTime.Now.ToString("MMM dd, yyyy");
    }
}
