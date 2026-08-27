using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace StardewAgentMod;

internal sealed class CompanionNpc : NPC
{
    public bool HasTextAboveHead => textAboveHeadTimer > 0 || textAboveHeadPreTimer > 0;

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

    public void ShowTextAboveHead(string text, int durationMs)
    {
        showTextAboveHead(text, null, 2, durationMs, 0);
    }

    public void ClearTextAboveHead()
    {
        clearTextAboveHead();
    }
}
