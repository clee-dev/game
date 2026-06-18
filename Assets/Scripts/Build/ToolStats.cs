/// <summary>
/// Build speed reference (Systems Architecture, Section 8.2). Shop speed upgrades
/// will apply a multiplier on top of these server-side when that system exists.
/// </summary>
public static class ToolStats
{
    public static float BuildDuration(ToolType tool)
    {
        switch (tool)
        {
            case ToolType.Hammer: return 2.0f;
            case ToolType.Trowel: return 2.5f;
            case ToolType.Torch:  return 4.0f;
            default:               return 2.0f;
        }
    }
}
