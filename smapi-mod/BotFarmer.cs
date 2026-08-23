using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace StardewAgentMod;

internal sealed class BotFarmer : Farmer
{
    public bool IsBot { get; } = true;

    public override void draw(SpriteBatch b)
    {
        // The paired CompanionNpc is the visible representation.
    }

    public override void SetMovingUp(bool value)
    {
        if (!value)
            Halt();
        else
            moveUp = true;
    }

    public override void SetMovingRight(bool value)
    {
        if (!value)
            Halt();
        else
            moveRight = true;
    }

    public override void SetMovingDown(bool value)
    {
        if (!value)
            Halt();
        else
            moveDown = true;
    }

    public override void SetMovingLeft(bool value)
    {
        if (!value)
            Halt();
        else
            moveLeft = true;
    }

    public new void tryToMoveInDirection(int direction, bool isFarmer, int damagesFarmer, bool glider)
    {
        if (currentLocation is null || !currentLocation.isTilePassable(nextPosition(direction), Game1.viewport))
            return;

        switch (direction)
        {
            case 0:
                position.Y -= speed + addedSpeed;
                break;
            case 1:
                position.X += speed + addedSpeed;
                break;
            case 2:
                position.Y += speed + addedSpeed;
                break;
            case 3:
                position.X -= speed + addedSpeed;
                break;
        }
    }

    public void FaceToward(Vector2 targetTile)
    {
        var difference = targetTile * Game1.tileSize - Position;
        FacingDirection = Math.Abs(difference.X) > Math.Abs(difference.Y)
            ? difference.X > 0 ? 1 : 3
            : difference.Y > 0 ? 2 : 0;
    }

    public void WakeUp()
    {
        isInBed.Value = false;
        sleptInTemporaryBed.Value = false;
        Stamina = MaxStamina;
        health = maxHealth;
    }

    public void SignalSleepReady()
    {
        isInBed.Value = true;
    }
}
