using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Tools;

namespace StardewAgentMod;

internal static class ToolExecutor
{
    public static void Execute(Tool tool, GameLocation location, Point targetTile, BotFarmer shadow)
    {
        if (tool is MeleeWeapon weapon)
        {
            var toolLocation = shadow.GetToolLocation(true);
            weapon.DoDamage(location, (int)toolLocation.X, (int)toolLocation.Y, shadow.FacingDirection, 1, shadow);
            return;
        }

        tool.DoFunction(
            location,
            targetTile.X * Game1.tileSize,
            targetTile.Y * Game1.tileSize,
            1,
            shadow);
    }
}
