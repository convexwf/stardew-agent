using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Monsters;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;

namespace StardewAgentMod;

internal static class CompanionObservationScanner
{
    public static ObservationInfo Scan(GameLocation location, Vector2 centerTile, int radius)
    {
        var centerX = (int)centerTile.X;
        var centerY = (int)centerTile.Y;
        var result = new ObservationInfo
        {
            Location = location.Name,
            Center = new TileDto { X = centerX, Y = centerY },
            Radius = radius
        };

        for (var x = centerX - radius; x <= centerX + radius; x++)
        {
            for (var y = centerY - radius; y <= centerY + radius; y++)
            {
                var tile = new Vector2(x, y);
                var tileInfo = ScanTile(location, tile);
                if (tileInfo is not null)
                    result.Tiles.Add(tileInfo);
            }
        }

        foreach (var character in location.characters)
        {
            var tile = character.Tile;
            if (Math.Abs(tile.X - centerTile.X) > radius || Math.Abs(tile.Y - centerTile.Y) > radius)
                continue;
            if (character is Monster monster)
            {
                result.Monsters.Add(new ObservationMonsterInfo
                {
                    Name = monster.Name,
                    X = (int)tile.X,
                    Y = (int)tile.Y,
                    Health = monster.Health,
                    MaxHealth = monster.MaxHealth
                });
            }
            else if (!string.Equals(character.Name, CompanionController.Id, StringComparison.OrdinalIgnoreCase))
            {
                result.Npcs.Add(new ObservationNpcInfo
                {
                    Name = character.Name,
                    X = (int)tile.X,
                    Y = (int)tile.Y
                });
            }
        }

        return result;
    }

    private static ObservationTileInfo? ScanTile(GameLocation location, Vector2 tile)
    {
        var x = (int)tile.X;
        var y = (int)tile.Y;
        var passable = location.isTilePassable(new xTile.Dimensions.Location(x, y), Game1.viewport);
        var water = location.isWaterTile(x, y);
        string? terrain = null;
        string? crop = null;
        var cropReady = false;
        var waterState = -1;

        if (location.terrainFeatures.TryGetValue(tile, out var feature))
        {
            if (feature is HoeDirt dirt)
            {
                terrain = "hoeDirt";
                waterState = dirt.state.Value;
                if (dirt.crop is not null)
                {
                    crop = dirt.crop.indexOfHarvest.Value;
                    try
                    {
                        var item = ItemRegistry.Create("(O)" + crop);
                        crop = item?.DisplayName ?? crop;
                    }
                    catch
                    {
                        // Keep the harvest identifier if the item registry cannot resolve it.
                    }
                    cropReady = dirt.readyForHarvest();
                }
            }
            else if (feature is Tree)
                terrain = "tree";
            else if (feature is Grass)
                terrain = "grass";
            else if (feature is Bush)
                terrain = "bush";
        }

        string? objectName = null;
        string? objectType = null;
        var breakable = false;
        var interactable = false;
        if (location.objects.TryGetValue(tile, out var obj))
        {
            objectName = obj.DisplayName ?? obj.Name;
            objectType = GetObjectType(obj);
            breakable = IsBreakable(obj);
            interactable = IsInteractable(obj);
        }

        if (terrain is null && objectName is null && !water && passable)
            return null;

        return new ObservationTileInfo
        {
            X = x,
            Y = y,
            Passable = passable,
            Water = water,
            Terrain = terrain,
            Crop = crop,
            CropReady = cropReady,
            WaterState = waterState,
            ObjectName = objectName,
            ObjectType = objectType,
            Breakable = breakable,
            Interactable = interactable
        };
    }

    private static string GetObjectType(StardewValley.Object obj)
    {
        if (obj is Chest)
            return "chest";
        if (obj.Name is not null)
        {
            if (obj.Name.Contains("Stone")) return "stone";
            if (obj.Name.Contains("Weed")) return "weed";
            if (obj.Name.Contains("Twig")) return "twig";
            if (obj.Name.Contains("Ladder") || obj.Name.Contains("Shaft")) return "ladder";
        }
        if (obj.bigCraftable.Value)
            return "machine";
        return "object";
    }

    private static bool IsBreakable(StardewValley.Object obj)
    {
        return obj.Name is not null && (obj.Name.Contains("Stone")
            || obj.Name.Contains("Weed")
            || obj.Name.Contains("Twig")
            || obj.ParentSheetIndex is 294 or 295 or 343 or 450);
    }

    private static bool IsInteractable(StardewValley.Object obj)
    {
        return obj is Chest
            || obj.bigCraftable.Value
            || (obj.Name is not null && (obj.Name.Contains("Ladder") || obj.Name.Contains("Shaft")));
    }
}
