using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Mutable in-memory blueprint model used only by the Level Editor. BlueprintData (the
/// runtime/serialized model, Blueprint JSON Schema) is array-based and treated as
/// immutable everywhere else in the codebase; this wraps the same fields in
/// editor-friendly collections (keyed by grid position for O(1) tile lookups) and
/// converts to/from BlueprintData at load/save time via FromBlueprintData/ToBlueprintData.
/// </summary>
public class EditableBlueprint
{
    public string Id = "blueprint_new";
    public string Name = "New Blueprint";
    public string Description = "";
    public string Scenery = "forest";
    public Vector3Int GridSize = new Vector3Int(10, 3, 10);

    public readonly Dictionary<Vector3Int, TileData> Tiles = new();
    public readonly List<SupplyZoneData> SupplyZones = new();
    public readonly List<OrderStationData> OrderStations = new();
    public readonly List<ToolDepotData> ToolDepots = new();
    public readonly List<WorldPosition> PlayerSpawns = new();

    public int TimeLimitSeconds = 300;
    public List<string> AllowedMaterials = new() { "wood" };
    public float CompletionFull = 1f;
    public float CompletionPartial = 0.75f;
    public int BasePayout = 500;

    private int _nextTileNumber = 1;
    private int _nextSupplyZoneNumber = 1;
    private int _nextOrderStationNumber = 1;
    private int _nextToolDepotNumber = 1;

    public string NextTileId() => $"tile_{_nextTileNumber++:D3}";
    public string NextSupplyZoneId() => $"supply_{_nextSupplyZoneNumber++:D3}";
    public string NextOrderStationId() => $"order_{_nextOrderStationNumber++:D3}";
    public string NextToolDepotId() => $"depot_{_nextToolDepotNumber++:D3}";

    public static EditableBlueprint FromBlueprintData(BlueprintData data)
    {
        var bp = new EditableBlueprint
        {
            Id = data.id,
            Name = data.name,
            Description = data.description ?? "",
            Scenery = data.scenery ?? "forest",
            GridSize = data.gridSize.ToVector3Int(),
        };

        foreach (TileData tile in data.tiles ?? Array.Empty<TileData>())
        {
            bp.Tiles[tile.position.ToVector3Int()] = tile;
            bp._nextTileNumber = Math.Max(bp._nextTileNumber, ExtractNumber(tile.id) + 1);
        }

        foreach (SupplyZoneData zone in data.supplyZones ?? Array.Empty<SupplyZoneData>())
        {
            bp.SupplyZones.Add(zone);
            bp._nextSupplyZoneNumber = Math.Max(bp._nextSupplyZoneNumber, ExtractNumber(zone.id) + 1);
        }

        foreach (OrderStationData station in data.orderStations ?? Array.Empty<OrderStationData>())
        {
            bp.OrderStations.Add(station);
            bp._nextOrderStationNumber = Math.Max(bp._nextOrderStationNumber, ExtractNumber(station.id) + 1);
        }

        foreach (ToolDepotData depot in data.toolDepots ?? Array.Empty<ToolDepotData>())
        {
            bp.ToolDepots.Add(depot);
            bp._nextToolDepotNumber = Math.Max(bp._nextToolDepotNumber, ExtractNumber(depot.id) + 1);
        }

        foreach (WorldPosition spawn in data.playerSpawns ?? Array.Empty<WorldPosition>())
            bp.PlayerSpawns.Add(spawn);

        if (data.contractDefaults != null)
        {
            bp.TimeLimitSeconds = data.contractDefaults.timeLimitSeconds;
            bp.AllowedMaterials = new List<string>(data.contractDefaults.allowedMaterials ?? Array.Empty<string>());
            bp.CompletionFull = data.contractDefaults.completionThresholds?.full ?? 1f;
            bp.CompletionPartial = data.contractDefaults.completionThresholds?.partial ?? 0.75f;
            bp.BasePayout = data.contractDefaults.basePayout;
        }

        return bp;
    }

    public BlueprintData ToBlueprintData()
    {
        return new BlueprintData
        {
            id = Id,
            name = Name,
            description = Description,
            scenery = Scenery,
            gridSize = new GridPosition { x = GridSize.x, y = GridSize.y, z = GridSize.z },
            tiles = Tiles.Values.ToArray(),
            supplyZones = SupplyZones.ToArray(),
            orderStations = OrderStations.ToArray(),
            toolDepots = ToolDepots.ToArray(),
            playerSpawns = PlayerSpawns.ToArray(),
            contractDefaults = new ContractDefaults
            {
                timeLimitSeconds = TimeLimitSeconds,
                allowedMaterials = AllowedMaterials.ToArray(),
                completionThresholds = new CompletionThresholds { full = CompletionFull, partial = CompletionPartial },
                basePayout = BasePayout,
            },
        };
    }

    private static int ExtractNumber(string id)
    {
        string digits = new string((id ?? "").Where(char.IsDigit).ToArray());
        return digits.Length > 0 && int.TryParse(digits, out int n) ? n : 0;
    }
}
