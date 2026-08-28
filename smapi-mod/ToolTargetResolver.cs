using Microsoft.Xna.Framework;
using StardewValley;

namespace StardewAgentMod;

internal readonly record struct ToolApproach(Point Tile, int FacingDirection);

internal static class ToolTargetResolver
{
    public static bool TryFindApproach(
        GameLocation location,
        Point targetTile,
        Point currentTile,
        out ToolApproach approach)
    {
        var candidates = new[]
        {
            new ToolApproach(new Point(targetTile.X, targetTile.Y + 1), 0),
            new ToolApproach(new Point(targetTile.X - 1, targetTile.Y), 1),
            new ToolApproach(new Point(targetTile.X, targetTile.Y - 1), 2),
            new ToolApproach(new Point(targetTile.X + 1, targetTile.Y), 3)
        };

        foreach (var candidate in candidates
            .OrderBy(item => Vector2.DistanceSquared(
                new Vector2(currentTile.X, currentTile.Y),
                new Vector2(item.Tile.X, item.Tile.Y))))
        {
            try
            {
                var mapTile = new xTile.Dimensions.Location(candidate.Tile.X, candidate.Tile.Y);
                if (location.isTilePassable(mapTile, Game1.viewport))
                {
                    approach = candidate;
                    return true;
                }
            }
            catch (Exception)
            {
                // A coordinate outside a location's map is simply not a valid approach.
            }
        }

        approach = default;
        return false;
    }
}
