using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace StardewAgentMod;

internal sealed class CompanionNpc : NPC
{
    private readonly Texture2D _bubblePixel;
    private string? _bubbleText;
    private int _bubbleTicks;

    public CompanionNpc(
        AnimatedSprite sprite,
        Vector2 position,
        string defaultMap,
        int facingDirection,
        string name,
        Texture2D portrait)
        : base(sprite, position, defaultMap, facingDirection, name, portrait, false)
    {
        _bubblePixel = new Texture2D(Game1.graphics.GraphicsDevice, 1, 1);
        _bubblePixel.SetData(new[] { Color.White });
    }

    public void ShowSpeechBubble(string text, int durationMs)
    {
        _bubbleText = text;
        _bubbleTicks = Math.Max(1, (int)Math.Ceiling(durationMs / 1000d * 60d));
    }

    public void TickSpeechBubble()
    {
        if (_bubbleTicks <= 0)
            return;

        _bubbleTicks--;
        if (_bubbleTicks == 0)
            _bubbleText = null;
    }

    public override void draw(SpriteBatch b, float alpha = 1f)
    {
        if (Sprite?.Texture is null || IsInvisible)
            return;

        var baseScale = Math.Max(0.2f, scale.Value) * 4f;
        var drawScale = new Vector2(baseScale);
        var localPosition = getLocalPosition(Game1.viewport);
        var widthOffset = GetSpriteWidthForPositioning() * 4f / 2f;
        var heightOffset = GetBoundingBox().Height / 2f;
        var screenPosition = localPosition + new Vector2(widthOffset, heightOffset);
        var origin = new Vector2(Sprite.SpriteWidth / 2f, Sprite.SpriteHeight * 3f / 4f);
        var layerDepth = Math.Max(0f, drawOnTop ? 0.991f : StandingPixel.Y / 10000f);
        var effects = flip
            || (Sprite.CurrentAnimation is not null
                && Sprite.currentAnimationIndex < Sprite.CurrentAnimation.Count
                && Sprite.CurrentAnimation[Sprite.currentAnimationIndex].flip)
            ? SpriteEffects.FlipHorizontally
            : SpriteEffects.None;

        b.Draw(
            Sprite.Texture,
            screenPosition,
            Sprite.SourceRect,
            Color.White * alpha,
            0f,
            origin,
            drawScale,
            effects,
            layerDepth);

        if (_bubbleText is null || _bubbleTicks <= 0 || Game1.eventUp || Game1.smallFont is null)
            return;

        DrawSpeechBubble(b, _bubbleText);
    }

    private void DrawSpeechBubble(SpriteBatch b, string text)
    {
        const float maxTextWidth = 240f;
        const int horizontalPadding = 10;
        const int verticalPadding = 7;
        const int tailHeight = 8;

        var font = Game1.smallFont;
        var lines = WrapText(font, text, maxTextWidth);
        if (lines.Count == 0)
            return;

        var textWidth = 0f;
        foreach (var line in lines)
            textWidth = Math.Max(textWidth, font.MeasureString(line).X);

        var bubbleWidth = Math.Max(80, (int)Math.Ceiling(textWidth) + horizontalPadding * 2);
        var bubbleHeight = font.LineSpacing * lines.Count + verticalPadding * 2;
        var localPosition = getLocalPosition(Game1.viewport);
        var centerX = localPosition.X + GetBoundingBox().Width / 2f;
        var bottomY = localPosition.Y - 32f - Sprite.SpriteHeight * 4f;
        var left = (int)Math.Round(centerX - bubbleWidth / 2f);
        var top = (int)Math.Round(bottomY - bubbleHeight - tailHeight);
        var background = new Rectangle(left, top, bubbleWidth, bubbleHeight);

        const float layerDepth = 0.9999f;
        DrawPixel(b, background, Color.White * 0.95f, layerDepth);
        DrawBubbleBorder(b, left, top, bubbleWidth, bubbleHeight, Color.Black * 0.85f, layerDepth);

        var tailX = (int)Math.Round(centerX);
        DrawPixel(b, new Rectangle(tailX - 5, top + bubbleHeight, 10, 4), Color.Black * 0.85f, layerDepth);
        DrawPixel(b, new Rectangle(tailX - 3, top + bubbleHeight + 4, 6, 4), Color.White * 0.95f, layerDepth);

        for (var index = 0; index < lines.Count; index++)
        {
            var lineSize = font.MeasureString(lines[index]);
            var position = new Vector2(
                left + (bubbleWidth - lineSize.X) / 2f,
                top + verticalPadding + index * font.LineSpacing
            );
            b.DrawString(font, lines[index], position, Color.Black, 0f, Vector2.Zero, 1f, SpriteEffects.None, layerDepth);
        }
    }

    private void DrawBubbleBorder(SpriteBatch b, int left, int top, int width, int height, Color color, float layerDepth)
    {
        const int border = 2;
        DrawPixel(b, new Rectangle(left - border, top - border, width + border * 2, border), color, layerDepth);
        DrawPixel(b, new Rectangle(left - border, top + height, width + border * 2, border), color, layerDepth);
        DrawPixel(b, new Rectangle(left - border, top, border, height), color, layerDepth);
        DrawPixel(b, new Rectangle(left + width, top, border, height), color, layerDepth);
    }

    private void DrawPixel(SpriteBatch b, Rectangle destination, Color color, float layerDepth)
    {
        b.Draw(_bubblePixel, destination, null, color, 0f, Vector2.Zero, SpriteEffects.None, layerDepth);
    }

    private static List<string> WrapText(SpriteFont font, string text, float maxWidth)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var character in text)
        {
            if (character == '\r')
                continue;
            if (character == '\n')
            {
                AddLine(lines, current);
                current = "";
                continue;
            }

            var next = current + character;
            if (current.Length > 0 && font.MeasureString(next).X > maxWidth)
            {
                AddLine(lines, current);
                current = character.ToString();
            }
            else
                current = next;
        }
        AddLine(lines, current);

        if (lines.Count > 4)
        {
            lines.RemoveRange(4, lines.Count - 4);
            var last = lines[3];
            while (last.Length > 0 && font.MeasureString(last + "…").X > maxWidth)
                last = last[..^1];
            lines[3] = last + "…";
        }

        return lines;
    }

    private static void AddLine(List<string> lines, string line)
    {
        if (line.Length > 0)
            lines.Add(line);
    }
}
