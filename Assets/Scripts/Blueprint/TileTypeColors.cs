using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared TileType -> Color lookup. Originally a private dict inside
/// LevelEditorController; extracted so HubTerminal's top-down blueprint preview
/// uses the exact same colors as the Level Editor's placeholder tile cubes.
/// </summary>
public static class TileTypeColors
{
    private static readonly Dictionary<TileType, Color> Colors = new()
    {
        { TileType.Foundation, new Color(0.5f, 0.5f, 0.5f) },
        { TileType.Floor,      new Color(0.6f, 0.4f, 0.2f) },
        { TileType.Wall,       new Color(0.8f, 0.7f, 0.5f) },
        { TileType.Window,     new Color(0.6f, 0.8f, 1f) },
        { TileType.Door,       new Color(0.9f, 0.5f, 0.1f) },
        { TileType.Column,     new Color(0.3f, 0.3f, 0.3f) },
        { TileType.Furniture,  new Color(0.3f, 0.7f, 0.3f) },
        { TileType.Decor,      new Color(0.6f, 0.3f, 0.7f) },
    };

    public static Color ColorFor(TileType type) =>
        Colors.TryGetValue(type, out Color color) ? color : Color.magenta;
}
