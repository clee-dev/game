using System;
using UnityEngine;

/// <summary>
/// Plain data classes mirroring the Blueprint JSON Schema exactly.
/// Public fields only (not properties) -- required for Newtonsoft to deserialize
/// reliably under Unity's IL2CPP/AOT constraints. Loaded by BlueprintLoader,
/// never edited at runtime -- BuildSystem copies what it needs into live
/// runtime structures (tilesByPosition, tilesByState, etc).
/// </summary>
[Serializable]
public class GridPosition
{
    public int x;
    public int y;
    public int z;

    public Vector3Int ToVector3Int() => new Vector3Int(x, y, z);
}

[Serializable]
public class WorldPosition
{
    public float x;
    public float y;
    public float z;

    public Vector3 ToVector3() => new Vector3(x, y, z);
}

[Serializable]
public class TileData
{
    public string id;
    public GridPosition position;
    public string type;             // TileType, lowercase (see BlueprintEnums)
    public string requiredMaterial; // MaterialType or "any"
    public string requiredTool;     // ToolType or "none"
    public int health;
}

[Serializable]
public class SupplyZoneData
{
    public string id;
    public WorldPosition worldPosition;
}

[Serializable]
public class OrderStationData
{
    public string id;
    public WorldPosition worldPosition;
}

[Serializable]
public class ToolDepotData
{
    public string id;
    public WorldPosition worldPosition;
    public string[] tools;
}

[Serializable]
public class CompletionThresholds
{
    public float full;
    public float partial;
}

[Serializable]
public class ContractDefaults
{
    public int timeLimitSeconds;
    public string[] allowedMaterials;
    public CompletionThresholds completionThresholds;
    public int basePayout;
}

[Serializable]
public class BlueprintData
{
    public string id;
    public string name;
    public string description;
    public string scenery;

    public GridPosition gridSize;

    public TileData[] tiles;
    public SupplyZoneData[] supplyZones;
    public OrderStationData[] orderStations;
    public ToolDepotData[] toolDepots;
    public WorldPosition[] playerSpawns;

    public ContractDefaults contractDefaults;
}
