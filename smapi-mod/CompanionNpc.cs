using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace StardewAgentMod;

internal sealed class CompanionNpc : NPC
{
    public CompanionNpc(
        AnimatedSprite sprite,
        Vector2 position,
        string defaultMap,
        int facingDirection,
        string name,
        Texture2D portrait)
        : base(sprite, position, defaultMap, facingDirection, name, portrait, false)
    {
    }
}
