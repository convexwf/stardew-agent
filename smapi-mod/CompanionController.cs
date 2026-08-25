using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Monsters;
using StardewValley.Objects;
using StardewValley.Pathfinding;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace StardewAgentMod;

internal sealed class CompanionController
{
    public const string Id = "companion-1";
    public const string DisplayName = "Companion";

    private readonly IModHelper _helper;
    private readonly IMonitor _monitor;
    private CompanionNpc? _visual;
    private BotFarmer? _shadow;
    private ActiveMove? _activeMove;
    private bool _autoCombat;
    private bool _fishingActive;
    private int _autoCombatCooldown;

    public CompanionController(IModHelper helper, IMonitor monitor)
    {
        _helper = helper;
        _monitor = monitor;
    }

    public bool IsSpawned => _visual is not null && _shadow is not null;

    public bool IsBusy => _activeMove is not null;

    public string? CurrentAction => _activeMove?.Action ?? (_fishingActive ? "cast_fishing_rod" : null);

    public bool AutoCombat => _autoCombat;

    public void EnsureSpawned()
    {
        if (IsSpawned || !Context.IsWorldReady || Game1.currentLocation is null || Game1.player is null)
            return;

        var location = Game1.currentLocation;
        var position = FindSpawnPosition(location);
        var portrait = _helper.GameContent.Load<Texture2D>("Portraits/Abigail");
        _visual = new CompanionNpc(
            new AnimatedSprite("Characters\\Abigail", 0, 16, 32),
            position,
            location.Name,
            2,
            Id,
            portrait)
        {
            displayName = DisplayName
        };
        location.addCharacter(_visual);

        _shadow = new BotFarmer
        {
            UniqueMultiplayerID = _helper.Multiplayer.GetNewID(),
            Name = Id + "-shadow",
            displayName = DisplayName,
            Speed = 2,
            Stamina = Farmer.startingStamina,
            MaxItems = 36
        };
        foreach (var tool in Farmer.initialTools())
            _shadow.Items.Add(tool);
        while (_shadow.Items.Count < _shadow.MaxItems)
            _shadow.Items.Add(null);

        SyncShadow();
        _monitor.Log(
            $"Spawned {DisplayName} in {location.Name} at pixel ({position.X:0},{position.Y:0}), "
            + $"tile ({_visual.Tile.X:0.0},{_visual.Tile.Y:0.0}), visible={!_visual.IsInvisible}, "
            + $"sprite_loaded={_visual.Sprite?.Texture is not null}.",
            LogLevel.Info);
    }

    private static Vector2 FindSpawnPosition(GameLocation location)
    {
        var playerTile = Game1.player.Tile;
        var candidates = new[]
        {
            new Vector2(playerTile.X + 1, playerTile.Y),
            new Vector2(playerTile.X - 1, playerTile.Y),
            new Vector2(playerTile.X, playerTile.Y - 1),
            new Vector2(playerTile.X, playerTile.Y + 1)
        };

        foreach (var tile in candidates)
        {
            var mapTile = new xTile.Dimensions.Location((int)tile.X, (int)tile.Y);
            if (location.isTilePassable(mapTile, Game1.viewport))
                return tile * Game1.tileSize;
        }

        return Game1.player.Position + new Vector2(Game1.tileSize, 0f);
    }

    public MoveCompletion? Tick()
    {
        if (!IsSpawned || _visual is null || _shadow is null)
            return null;

        SyncShadow();
        _visual.TickSpeechBubble();
        TickFishing();
        TickAutoCombat();

        if (_activeMove is null)
            return null;

        _activeMove.ElapsedTicks++;
        var current = ReadTile();
        var reached = current.X == _activeMove.Target.X && current.Y == _activeMove.Target.Y;
        var pathEnded = _visual.controller is null && _activeMove.ElapsedTicks > 2;
        var timedOut = _activeMove.ElapsedTicks > Math.Max(120, _activeMove.Ticks * 120);
        if (!reached && !pathEnded && !timedOut)
            return null;

        var active = _activeMove;
        _activeMove = null;
        _visual.controller = null;
        var after = ReadTile();
        var moved = active.Before.X != after.X || active.Before.Y != after.Y;
        return new MoveCompletion
        {
            RequestId = active.RequestId,
            Action = active.Action,
            Direction = active.Direction,
            Ticks = active.Ticks,
            TargetTile = new TileDto { X = active.Target.X, Y = active.Target.Y },
            Status = reached && !timedOut ? "succeeded" : "blocked",
            BeforeTile = active.Before,
            AfterTile = after,
            Moved = moved,
            WorldReady = Context.IsWorldReady,
            Error = timedOut
                ? new ErrorDetail { Code = "movement_timeout", Message = "the companion did not reach the target tile" }
                : null
        };
    }

    public bool TryStartMove(string requestId, string direction, int ticks, out MoveCompletion? failure)
    {
        failure = null;
        if (!TryReadDirection(direction, out var offset))
        {
            failure = MoveCompletion.Failed(requestId, "move_relative", direction, ticks, "invalid_direction", "direction must be up, down, left, or right");
            return false;
        }
        if (ticks is < 1 or > 30)
        {
            failure = MoveCompletion.Failed(requestId, "move_relative", direction, ticks, "invalid_ticks", "ticks must be between 1 and 30");
            return false;
        }
        if (!TryGetReady(out failure, requestId, "move_relative", direction, ticks))
            return false;

        var before = ReadTile();
        var distance = Math.Max(1, ticks / 5);
        var target = new Point(before.X + offset.X * distance, before.Y + offset.Y * distance);
        return TryStartPath(requestId, "move_relative", direction, ticks, target, before, out failure);
    }

    public bool TryStartMoveTo(string requestId, int x, int y, out MoveCompletion? failure)
    {
        failure = null;
        if (!TryGetReady(out failure, requestId, "move_to"))
            return false;

        var before = ReadTile();
        return TryStartPath(requestId, "move_to", null, 0, new Point(x, y), before, out failure);
    }

    public MoveCompletion? Cancel(string code, string message)
    {
        if (_activeMove is null || _visual is null)
            return null;

        var active = _activeMove;
        _activeMove = null;
        _visual.controller = null;
        return MoveCompletion.Failed(active.RequestId, active.Action, active.Direction, active.Ticks, code, message, "cancelled");
    }

    public bool TryFaceDirection(string direction, out ErrorDetail? error)
    {
        error = null;
        if (!TryGetReady(out var failure))
        {
            error = failure?.Error;
            return false;
        }
        if (!TryReadDirection(direction, out var offset))
        {
            error = new ErrorDetail { Code = "invalid_direction", Message = "direction must be up, down, left, or right" };
            return false;
        }

        _visual!.FacingDirection = offset switch
        {
            { X: 0, Y: -1 } => 0,
            { X: 1, Y: 0 } => 1,
            { X: 0, Y: 1 } => 2,
            _ => 3
        };
        SyncShadow();
        return true;
    }

    public bool TryUseTool(string toolName, int x, int y, out object? data, out ErrorDetail? error)
    {
        data = null;
        error = null;
        if (!TryGetReady(out var failure))
        {
            error = failure?.Error;
            return false;
        }

        var toolType = (toolName ?? "").ToLowerInvariant() switch
        {
            "pickaxe" => typeof(Pickaxe),
            "axe" => typeof(Axe),
            "hoe" => typeof(Hoe),
            "watering_can" or "wateringcan" => typeof(WateringCan),
            "sword" or "weapon" => typeof(MeleeWeapon),
            _ => null
        };
        if (toolType is null)
        {
            error = new ErrorDetail { Code = "unknown_tool", Message = $"unknown tool: {toolName}" };
            return false;
        }

        var success = UseToolAt(new Vector2(x, y), toolType);
        data = new { tool = toolName, tile = new TileDto { X = x, Y = y }, used = success };
        if (!success)
            error = new ErrorDetail { Code = "tool_use_failed", Message = $"failed to use {toolName} at ({x},{y})" };
        return success;
    }

    public bool TryInteract(int x, int y, out object? data, out ErrorDetail? error)
    {
        data = null;
        error = null;
        if (!TryGetReady(out var failure))
        {
            error = failure?.Error;
            return false;
        }

        var location = _visual!.currentLocation!;
        var tile = new Vector2(x, y);
        if (location.terrainFeatures.TryGetValue(tile, out var feature)
            && feature is HoeDirt dirt
            && dirt.crop is not null
            && dirt.readyForHarvest())
        {
            var harvested = dirt.crop.harvest(x, y, dirt, null);
            data = new { interaction = "harvest", tile = new TileDto { X = x, Y = y }, success = harvested };
            if (!harvested)
                error = new ErrorDetail { Code = "harvest_failed", Message = "the crop could not be harvested" };
            return harvested;
        }

        if (!location.objects.TryGetValue(tile, out var obj))
        {
            error = new ErrorDetail { Code = "nothing_to_interact", Message = $"nothing to interact with at ({x},{y})" };
            return false;
        }

        if (obj is Chest chest)
        {
            var items = chest.Items
                .Where(item => item is not null)
                .Select(item => new InventoryItemInfo
                {
                    Slot = -1,
                    Name = item!.Name ?? "",
                    DisplayName = item.DisplayName ?? item.Name ?? "",
                    QualifiedId = item.QualifiedItemId,
                    Stack = item.Stack,
                    Type = item is Tool ? "tool" : "item",
                    Edibility = item is StardewValley.Object food ? food.Edibility : -1
                })
                .ToList();
            data = new { interaction = "chest", tile = new TileDto { X = x, Y = y }, items };
            return true;
        }

        if (obj.Name is not null && (obj.Name.Contains("Ladder") || obj.Name.Contains("Shaft")))
        {
            if (location is MineShaft shaft)
            {
                var nextLevel = shaft.mineLevel + 1;
                var nextName = "UndergroundMine" + nextLevel;
                var nextLocation = Game1.getLocationFromName(nextName)
                    ?? MineShaft.GetMine(nextName);
                if (nextLocation is not null)
                {
                    var warped = TryWarp(nextLocation.Name, 6, 6, out _, out var warpError);
                    if (!warped)
                    {
                        error = warpError ?? new ErrorDetail { Code = "warp_failed", Message = "the Companion could not enter the next mine level" };
                        return false;
                    }
                    data = new
                    {
                        interaction = "ladder",
                        tile = new TileDto { X = x, Y = y },
                        destination = nextLocation.Name,
                        success = true
                    };
                    return true;
                }
            }

            error = new ErrorDetail { Code = "ladder_failed", Message = "the next mine level could not be loaded" };
            return false;
        }

        var success = obj.checkForAction(_shadow!);
        data = new { interaction = "object", object_name = obj.DisplayName ?? obj.Name, tile = new TileDto { X = x, Y = y }, success };
        if (!success)
            error = new ErrorDetail { Code = "interaction_failed", Message = $"could not interact with {obj.DisplayName ?? obj.Name}" };
        return success;
    }

    public bool TryWarp(string locationName, int x, int y, out object? data, out ErrorDetail? error)
    {
        data = null;
        error = null;
        if (!TryGetReady(out var failure))
        {
            error = failure?.Error;
            return false;
        }
        if (_activeMove is not null)
        {
            error = new ErrorDetail { Code = "busy", Message = "the companion is currently moving" };
            return false;
        }

        var target = Game1.getLocationFromName(locationName);
        if (target is null)
        {
            error = new ErrorDetail { Code = "unknown_location", Message = $"location not found: {locationName}" };
            return false;
        }

        _visual!.currentLocation?.characters.Remove(_visual);
        _visual.controller = null;
        var position = new Vector2(x * Game1.tileSize, y * Game1.tileSize);
        _visual.Position = position;
        _visual.currentLocation = target;
        target.addCharacter(_visual);
        _shadow!.Position = position;
        _shadow.currentLocation = target;
        SyncShadow();
        data = new { location = target.Name, tile = new TileDto { X = x, Y = y } };
        _monitor.Log($"Warped {DisplayName} to {target.Name} ({x},{y}).", LogLevel.Info);
        return true;
    }

    public bool TryAttack(out object? data, out ErrorDetail? error)
    {
        data = null;
        error = null;
        if (!TryGetReady(out var failure))
        {
            error = failure?.Error;
            return false;
        }

        var weapon = _shadow!.Items.FirstOrDefault(item => item is MeleeWeapon) as MeleeWeapon;
        var monster = FindNearestMonster();
        if (weapon is null || monster is null)
        {
            error = new ErrorDetail { Code = "no_attack_target", Message = weapon is null ? "the companion has no weapon" : "no monster in range" };
            data = new { attacked = false };
            return false;
        }

        _shadow.FaceToward(monster.Tile);
        try
        {
            var toolLocation = _shadow.GetToolLocation(true);
            weapon.DoDamage(_shadow.currentLocation, (int)toolLocation.X, (int)toolLocation.Y, _shadow.FacingDirection, 1, _shadow);
            data = new { attacked = true, monster = monster.Name, tile = new TileDto { X = (int)monster.Tile.X, Y = (int)monster.Tile.Y } };
            return true;
        }
        catch (Exception exception)
        {
            error = new ErrorDetail { Code = "attack_failed", Message = exception.Message };
            return false;
        }
    }

    public bool TryCastFishingRod(out object? data, out ErrorDetail? error)
    {
        data = null;
        error = null;
        if (!TryGetReady(out var failure))
        {
            error = failure?.Error;
            return false;
        }

        var rod = _shadow!.Items.FirstOrDefault(item => item is FishingRod) as FishingRod;
        if (rod is null)
        {
            error = new ErrorDetail { Code = "no_fishing_rod", Message = "the companion has no fishing rod" };
            return false;
        }
        try
        {
            rod.beginUsing(_shadow.currentLocation, (int)_shadow.Position.X, (int)_shadow.Position.Y, _shadow);
            rod.castingPower = 1f;
            _fishingActive = true;
            data = new { cast = true };
            return true;
        }
        catch (Exception exception)
        {
            error = new ErrorDetail { Code = "fishing_failed", Message = exception.Message };
            return false;
        }
    }

    public bool TrySetAutoCombat(bool enabled, out ErrorDetail? error)
    {
        error = null;
        if (!TryGetReady(out var failure))
        {
            error = failure?.Error;
            return false;
        }
        _autoCombat = enabled;
        return true;
    }

    public bool TryShowBubble(string text, int durationMs, out ErrorDetail? error)
    {
        error = null;
        EnsureSpawned();
        if (!IsSpawned || _visual is null || _shadow is null || _visual.currentLocation is null)
        {
            error = new ErrorDetail { Code = "world_not_ready", Message = "the companion is not spawned" };
            return false;
        }

        _visual.ShowSpeechBubble(text, durationMs);
        return true;
    }

    public bool TryEatItem(int? slot, out object? data, out ErrorDetail? error)
    {
        data = null;
        error = null;
        if (!TryGetReady(out var failure))
        {
            error = failure?.Error;
            return false;
        }

        StardewValley.Object? food = null;
        if (slot is >= 0 && slot < _shadow!.Items.Count && _shadow.Items[slot.Value] is StardewValley.Object selected && selected.Edibility > 0)
            food = selected;
        else
            food = _shadow!.Items.FirstOrDefault(item => item is StardewValley.Object candidate && candidate.Edibility > 0) as StardewValley.Object;

        if (food is null)
        {
            error = new ErrorDetail { Code = "no_edible_item", Message = "the companion has no edible item" };
            return false;
        }

        try
        {
            _shadow.eatObject(food);
            data = new { ate = true, item = food.DisplayName ?? food.Name };
            return true;
        }
        catch (Exception exception)
        {
            _shadow.Stamina = Math.Min(_shadow.MaxStamina, _shadow.Stamina + food.Edibility);
            _shadow.health = Math.Min(_shadow.maxHealth, _shadow.health + (int)(food.Edibility * 0.4f));
            food.Stack--;
            if (food.Stack <= 0)
                _shadow.Items[_shadow.Items.IndexOf(food)] = null;
            data = new { ate = true, item = food.DisplayName ?? food.Name, fallback = true };
            _monitor.Log($"Fallback eat implementation used: {exception.Message}", LogLevel.Debug);
            return true;
        }
    }

    public List<InventoryItemInfo> GetInventory()
    {
        if (_shadow is null)
            return new List<InventoryItemInfo>();
        return _shadow.Items
            .Select((item, index) => item is null ? null : new InventoryItemInfo
            {
                Slot = index,
                Name = item.Name ?? "",
                DisplayName = item.DisplayName ?? item.Name ?? "",
                QualifiedId = item.QualifiedItemId,
                Stack = item.Stack,
                Type = item is Tool ? "tool" : item is StardewValley.Object food && food.Edibility > 0 ? "food" : "item",
                Edibility = item is StardewValley.Object edible ? edible.Edibility : -1
            })
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();
    }

    public ObservationInfo? GetObservation(int radius)
    {
        var location = _visual?.currentLocation ?? Game1.player?.currentLocation;
        return location is null || _visual is null ? null : CompanionObservationScanner.Scan(location, _visual.Tile, radius);
    }

    public CompanionInfo GetInfo()
    {
        var inventory = GetInventory();
        return new CompanionInfo
        {
            Id = Id,
            DisplayName = DisplayName,
            Location = _visual?.currentLocation?.Name ?? "",
            Tile = _visual is null ? new TileDto() : ReadTile(),
            FacingDirection = FacingName(_visual?.FacingDirection ?? 2),
            Health = _shadow?.health ?? 0,
            MaxHealth = _shadow?.maxHealth ?? 0,
            Stamina = _shadow?.Stamina ?? 0,
            MaxStamina = _shadow?.MaxStamina ?? 0,
            InventoryCount = inventory.Count,
            Inventory = inventory,
            Mode = "direct",
            Status = IsBusy ? "moving" : _fishingActive ? "fishing" : _autoCombat ? "auto-combat" : "idle",
            CurrentAction = CurrentAction,
            WorldReady = Context.IsWorldReady && IsSpawned,
            Busy = IsBusy,
            AutoCombat = _autoCombat,
            Capabilities = new List<string>
            {
                "move_relative", "move_to", "face_direction", "use_tool", "interact", "warp_to",
                "observe", "get_inventory", "attack", "cast_fishing_rod", "set_auto_combat", "eat_item", "say", "bubble", "cancel"
            }
        };
    }

    public void SignalSleepReady()
    {
        _shadow?.SignalSleepReady();
    }

    public void WakeUp()
    {
        _shadow?.WakeUp();
        _fishingActive = false;
    }

    public void Cleanup()
    {
        CancelMove();
        if (_visual is not null)
            _visual.currentLocation?.characters.Remove(_visual);
        _visual = null;
        _shadow = null;
        _fishingActive = false;
        _autoCombat = false;
        _autoCombatCooldown = 0;
    }

    private bool TryGetReady(
        out MoveCompletion? failure,
        string requestId = "unknown",
        string action = "action",
        string? direction = null,
        int ticks = 0)
    {
        failure = null;
        EnsureSpawned();
        if (!IsSpawned || _visual is null || _shadow is null || _visual.currentLocation is null)
        {
            failure = MoveCompletion.Failed(requestId, action, direction, ticks, "world_not_ready", "the companion is not spawned");
            return false;
        }
        if (_activeMove is not null)
        {
            failure = MoveCompletion.Failed(requestId, action, direction, ticks, "busy", "the companion is already moving");
            return false;
        }
        return true;
    }

    private bool TryStartPath(string requestId, string action, string? direction, int ticks, Point target, TileDto before, out MoveCompletion? failure)
    {
        failure = null;
        if (_visual is null || _visual.currentLocation is null)
        {
            failure = MoveCompletion.Failed(requestId, action, direction, ticks, "world_not_ready", "the companion is not spawned");
            return false;
        }
        if (_activeMove is not null)
        {
            failure = MoveCompletion.Failed(requestId, action, direction, ticks, "busy", "the companion is already moving");
            return false;
        }
        try
        {
            _visual.controller = new PathFindController(_visual, _visual.currentLocation, target, 2);
            _activeMove = new ActiveMove(requestId, action, direction, ticks, before, target);
            return true;
        }
        catch (Exception error)
        {
            failure = MoveCompletion.Failed(requestId, action, direction, ticks, "pathfinding_failed", error.Message);
            return false;
        }
    }

    private bool TryReadDirection(string direction, out Point offset)
    {
        offset = (direction ?? "").ToLowerInvariant() switch
        {
            "up" => new Point(0, -1),
            "down" => new Point(0, 1),
            "left" => new Point(-1, 0),
            "right" => new Point(1, 0),
            _ => new Point(int.MinValue, int.MinValue)
        };
        return offset.X != int.MinValue;
    }

    private bool UseToolAt(Vector2 tile, Type toolType)
    {
        if (_shadow is null || _shadow.Stamina <= 0)
            return false;
        var tool = _shadow.Items.FirstOrDefault(item => item is not null && toolType.IsInstanceOfType(item)) as Tool;
        if (tool is null || _shadow.currentLocation is null)
            return false;

        _shadow.FaceToward(tile);
        var oldStamina = _shadow.Stamina;
        try
        {
            if (tool is MeleeWeapon weapon)
            {
                var toolLocation = _shadow.GetToolLocation(true);
                weapon.DoDamage(_shadow.currentLocation, (int)toolLocation.X, (int)toolLocation.Y, _shadow.FacingDirection, 1, _shadow);
            }
            else
            {
                tool.DoFunction(_shadow.currentLocation, (int)(tile.X * Game1.tileSize), (int)(tile.Y * Game1.tileSize), 1, _shadow);
            }
            _shadow.checkForExhaustion(oldStamina);
            return true;
        }
        catch (Exception error)
        {
            _monitor.Log($"Tool use failed: {error.Message}", LogLevel.Debug);
            return false;
        }
    }

    private Monster? FindNearestMonster(float range = 256f)
    {
        if (_shadow?.currentLocation is null)
            return null;
        Monster? nearest = null;
        var nearestDistance = range;
        foreach (var character in _shadow.currentLocation.characters)
        {
            if (character is not Monster monster || monster.IsInvisible || monster.Health <= 0)
                continue;
            var distance = Vector2.Distance(_shadow.Position, monster.Position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = monster;
            }
        }
        return nearest;
    }

    private void TickAutoCombat()
    {
        if (!_autoCombat || _autoCombatCooldown > 0)
        {
            if (_autoCombatCooldown > 0)
                _autoCombatCooldown--;
            return;
        }
        TryAttack(out _, out _);
        _autoCombatCooldown = 15;
    }

    private void TickFishing()
    {
        if (!_fishingActive || _shadow is null || Game1.currentGameTime is null)
            return;
        var rod = _shadow.Items.FirstOrDefault(item => item is FishingRod) as FishingRod;
        if (rod is null)
        {
            _fishingActive = false;
            return;
        }
        try
        {
            rod.tickUpdate(Game1.currentGameTime, _shadow);
            if (rod.isNibbling && !rod.isReeling && !rod.hit && !rod.pullingOutOfWater)
            {
                rod.DoFunction(_shadow.currentLocation, 1, 1, 1, _shadow);
                _fishingActive = false;
            }
        }
        catch
        {
            _fishingActive = false;
        }
    }

    private void CancelMove()
    {
        _activeMove = null;
        if (_visual is not null)
            _visual.controller = null;
    }

    private void SyncShadow()
    {
        if (_visual is null || _shadow is null)
            return;
        _shadow.Position = _visual.Position;
        _shadow.currentLocation = _visual.currentLocation;
        _shadow.FacingDirection = _visual.FacingDirection;
    }

    private TileDto ReadTile()
    {
        return new TileDto
        {
            X = (int)(_visual?.Tile.X ?? 0),
            Y = (int)(_visual?.Tile.Y ?? 0)
        };
    }

    private static string FacingName(int direction)
    {
        return direction switch
        {
            0 => "up",
            1 => "right",
            2 => "down",
            3 => "left",
            _ => "down"
        };
    }

    private sealed class ActiveMove
    {
        public ActiveMove(string requestId, string action, string? direction, int ticks, TileDto before, Point target)
        {
            RequestId = requestId;
            Action = action;
            Direction = direction;
            Ticks = ticks;
            Before = before;
            Target = target;
        }

        public string RequestId { get; }
        public string Action { get; }
        public string? Direction { get; }
        public int Ticks { get; }
        public TileDto Before { get; }
        public Point Target { get; }
        public int ElapsedTicks { get; set; }
    }
}
