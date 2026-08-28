using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace StardewAgentMod;

internal static class ToolVerifier
{
    public static bool TryVerify(
        ToolActionTask task,
        GameLocation location,
        out string verification,
        out bool contradicted)
    {
        verification = "api_returned";
        contradicted = false;
        var tile = new Vector2(task.TargetTile.X, task.TargetTile.Y);

        if (task.ToolType == typeof(WateringCan)
            && location.terrainFeatures.TryGetValue(tile, out var waterFeature)
            && waterFeature is HoeDirt waterDirt)
        {
            if (waterDirt.crop is null)
                return false;
            if (!waterDirt.needsWatering())
            {
                verification = "crop_watered";
                return true;
            }
            verification = "watering_target_unchanged";
            contradicted = true;
            return false;
        }

        if (task.ToolType == typeof(Axe) && task.BeforeTreeHealth.HasValue)
        {
            var tree = location.terrainFeatures.TryGetValue(tile, out var treeFeature)
                ? treeFeature as Tree
                : null;
            if (tree is null || tree.stump.Value || tree.health.Value < task.BeforeTreeHealth.Value)
            {
                verification = "tree_health_changed";
                return true;
            }
            verification = "tree_unchanged";
            contradicted = true;
            return false;
        }

        if (task.ToolType == typeof(Pickaxe) && task.BeforeBreakableStone)
        {
            var stillBreakable = location.objects.TryGetValue(tile, out var targetObject)
                && targetObject.IsBreakableStone();
            if (!stillBreakable)
            {
                verification = "stone_state_changed";
                return true;
            }
            // A single hit may not destroy a stone. Let the caller distinguish
            // an API call with no final state change from a failed API call.
            verification = "api_returned";
            return false;
        }

        return false;
    }
}
