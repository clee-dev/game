using System.Collections.Generic;

[System.Serializable]
public class SaveData
{
    public int          currency         = 0;
    public List<int>    ownedCosmeticIds = new List<int>();
    public PlayerStats  stats            = new PlayerStats();
    public List<SaveSlot> saveSlots      = new List<SaveSlot>();
    public int          activeSlotIndex  = -1;
}

[System.Serializable]
public class PlayerStats
{
    public int gamesPlayed = 0;
    public int wins        = 0;
}
