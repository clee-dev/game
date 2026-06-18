using System;

/// <summary>
/// Runtime-facing enums for the gameplay layer. Blueprint JSON stores these as
/// lowercase strings (see TileData/ToolDepotData) -- Parse() converts between
/// the two so gameplay code never deals with raw strings.
/// </summary>
// Furniture = ground furniture (beds, chairs -- needs the tile below built, same
// rule as Floor/Wall/Column). Decor = wall-mounted furniture (paintings, hanging
// pots -- needs an adjacent built tile, same rule as Window/Door).
public enum TileType { Foundation, Floor, Wall, Window, Door, Column, Furniture, Decor }

public enum MaterialType { Wood, Concrete, Steel, Any }

public enum ToolType { Hammer, Trowel, Torch, None }

public enum TileState { Empty, MaterialPlaced, Built, Destroyed }

public enum MaterialState { Loose, Held, Placed, Built }

public static class BlueprintEnums
{
    public static TileType ParseTileType(string raw) =>
        (TileType)Enum.Parse(typeof(TileType), raw, ignoreCase: true);

    public static MaterialType ParseMaterialType(string raw) =>
        (MaterialType)Enum.Parse(typeof(MaterialType), raw, ignoreCase: true);

    public static ToolType ParseToolType(string raw) =>
        (ToolType)Enum.Parse(typeof(ToolType), raw, ignoreCase: true);
}
