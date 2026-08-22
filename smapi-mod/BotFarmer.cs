using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace StardewAgentMod;

internal sealed class BotFarmer : Farmer
{
    public override void draw(SpriteBatch b)
    {
        // The paired CompanionNpc is the visible representation.
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
