using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace StardewAgentMod;

internal sealed class CompanionNpc : NPC
{
    public const string SwingPresentation = "swing";
    public const string MeleePresentation = "melee";
    public const string WaterPresentation = "water";
    public const string CastPresentation = "cast";

    private Texture2D? _pixel;
    private string? _presentationRequestId;
    private string _presentationKind = "none";
    private string? _presentationTool;
    private int _presentationFacing;
    private Point _presentationTargetTile;
    private int _presentationTotalTicks;
    private int _presentationRemainingTicks;
    private Texture2D? _presentationTexture;
    private Rectangle _presentationSourceRect;

    public bool HasTextAboveHead => textAboveHeadTimer > 0 || textAboveHeadPreTimer > 0;

    public bool HasActionPresentation => _presentationRemainingTicks > 0;

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

    public void ShowActionPresentation(
        string requestId,
        string kind,
        string? tool,
        int facing,
        Point targetTile,
        int totalTicks,
        Texture2D? texture,
        Rectangle sourceRect)
    {
        _presentationRequestId = requestId;
        _presentationKind = kind;
        _presentationTool = tool;
        _presentationFacing = facing;
        _presentationTargetTile = targetTile;
        _presentationTotalTicks = Math.Max(1, totalTicks);
        _presentationRemainingTicks = _presentationTotalTicks;
        _presentationTexture = texture;
        _presentationSourceRect = sourceRect;
    }

    public void TickActionPresentation()
    {
        if (_presentationRemainingTicks <= 0)
            return;
        _presentationRemainingTicks--;
        if (_presentationRemainingTicks == 0)
            ClearActionPresentation();
    }

    public void ClearActionPresentation()
    {
        _presentationRequestId = null;
        _presentationKind = "none";
        _presentationTool = null;
        _presentationFacing = 0;
        _presentationTargetTile = Point.Zero;
        _presentationTotalTicks = 0;
        _presentationRemainingTicks = 0;
        _presentationTexture = null;
        _presentationSourceRect = Rectangle.Empty;
    }

    public override void draw(SpriteBatch b, float alpha = 1f)
    {
        base.draw(b, alpha);
        DrawActionPresentation(b);
    }

    private void DrawActionPresentation(SpriteBatch b)
    {
        if (!HasActionPresentation || IsInvisible)
            return;

        var progress = 1f - (float)_presentationRemainingTicks / _presentationTotalTicks;
        if (_presentationKind == WaterPresentation)
            DrawWaterOverlay(b, progress);
        else
            DrawSwingOverlay(b, progress);
    }

    private void DrawSwingOverlay(SpriteBatch b, float progress)
    {
        var local = getLocalPosition(Game1.viewport);
        var widthOffset = GetSpriteWidthForPositioning() * 4f / 2f;
        var anchor = local + new Vector2(widthOffset, GetBoundingBox().Height * 0.45f);
        var mirror = _presentationFacing == 3;
        var eased = 1f - (1f - progress) * (1f - progress);
        var fromAngle = mirror ? 2.2f : -2.2f;
        var toAngle = mirror ? -0.9f : 0.9f;
        var angle = MathHelper.Lerp(fromAngle, toAngle, eased);
        var swingRadius = Game1.tileSize * 0.9f;
        var direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
        var depthBase = Math.Max(0f, StandingPixel.Y / 10000f);
        var depth = progress < 0.45f
            ? Math.Max(0f, depthBase - 0.002f)
            : Math.Min(0.999f, depthBase + 0.002f);
        var effects = mirror ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        const float scale = 4f;

        for (var i = 1; i <= 2; i++)
        {
            var ghostProgress = progress - 0.18f * i;
            if (ghostProgress <= 0f)
                break;
            var ghostEased = 1f - (1f - ghostProgress) * (1f - ghostProgress);
            var ghostAngle = MathHelper.Lerp(fromAngle, toAngle, ghostEased);
            var ghostDirection = new Vector2((float)Math.Cos(ghostAngle), (float)Math.Sin(ghostAngle));
            var ghostPosition = anchor + ghostDirection * swingRadius;
            DrawToolIcon(b, ghostPosition, ghostAngle, effects, scale, Color.White * (0.22f - 0.07f * i), Math.Max(0f, depth - 0.001f));
        }

        var toolPosition = anchor + direction * swingRadius;
        DrawToolIcon(b, toolPosition, angle, effects, scale, Color.White, depth);

        if (_presentationKind == MeleePresentation && progress >= 0.72f)
            DrawHitFlash(b, progress);
    }

    private void DrawToolIcon(
        SpriteBatch b,
        Vector2 position,
        float rotation,
        SpriteEffects effects,
        float scale,
        Color color,
        float layerDepth)
    {
        if (_presentationTexture is null || _presentationSourceRect == Rectangle.Empty)
        {
            DrawSwingArc(b, position, rotation, scale, color, layerDepth);
            return;
        }

        var origin = new Vector2(_presentationSourceRect.Width / 2f, _presentationSourceRect.Height * 0.8f);
        b.Draw(_presentationTexture, position, _presentationSourceRect, color, rotation, origin, scale, effects, layerDepth);
    }

    private void DrawSwingArc(SpriteBatch b, Vector2 position, float rotation, float scale, Color color, float layerDepth)
    {
        var pixel = GetPixel();
        var length = Game1.tileSize * 0.8f;
        var direction = new Vector2((float)Math.Cos(rotation), (float)Math.Sin(rotation));
        var start = position;
        var end = position + direction * length;
        var thickness = Math.Max(2f, Game1.tileSize * 0.12f * scale / 4f);
        b.Draw(pixel, new Rectangle((int)start.X, (int)start.Y, (int)Math.Max(1f, length), (int)thickness), null,
            color * 0.9f, rotation, new Vector2(0f, 0.5f), SpriteEffects.None, layerDepth);
        b.Draw(pixel, new Rectangle((int)end.X, (int)end.Y, (int)(length * 0.35f), (int)Math.Max(1f, thickness * 0.7f)), null,
            color * 0.7f, rotation, new Vector2(0f, 0.5f), SpriteEffects.None, layerDepth + 0.0001f);
    }

    private void DrawWaterOverlay(SpriteBatch b, float progress)
    {
        var local = getLocalPosition(Game1.viewport);
        var widthOffset = GetSpriteWidthForPositioning() * 4f / 2f;
        var start = local + new Vector2(widthOffset, GetBoundingBox().Height * 0.35f);
        var end = Game1.GlobalToLocal(new Vector2(
            _presentationTargetTile.X * Game1.tileSize,
            _presentationTargetTile.Y * Game1.tileSize));
        var pixel = GetPixel();
        var depth = Math.Max(0f, StandingPixel.Y / 10000f) + 0.002f;
        const int dropCount = 9;
        for (var i = 0; i < dropCount; i++)
        {
            var t = (i + 1f) / (dropCount + 1f) * (progress * 1.25f);
            if (t > 1f)
                break;
            var dropPosition = Vector2.Lerp(start, end, t);
            dropPosition.Y -= (float)Math.Sin(t * Math.PI) * Game1.tileSize * 0.55f;
            var size = MathHelper.Lerp(3f, 6f, t);
            b.Draw(pixel, new Rectangle((int)dropPosition.X, (int)dropPosition.Y, (int)size, (int)(size * 1.6f)), null,
                new Color(90, 150, 220) * 0.85f, 0f, Vector2.Zero, SpriteEffects.None, depth);
        }

        if (progress >= 0.8f)
        {
            for (var i = 0; i < 4; i++)
            {
                var splashT = (float)i / 3f;
                var offset = new Vector2((splashT - 0.5f) * Game1.tileSize * 0.8f, -splashT * Game1.tileSize * 0.45f);
                b.Draw(pixel, new Rectangle((int)(end.X + offset.X), (int)(end.Y + offset.Y), 4, 5), null,
                    new Color(90, 150, 220) * (0.9f - 0.2f * splashT), 0f, Vector2.Zero, SpriteEffects.None, depth + 0.0001f);
            }
        }
    }

    private void DrawHitFlash(SpriteBatch b, float progress)
    {
        var pixel = GetPixel();
        var center = Game1.GlobalToLocal(new Vector2(
            _presentationTargetTile.X * Game1.tileSize + Game1.tileSize / 2f,
            _presentationTargetTile.Y * Game1.tileSize + Game1.tileSize / 2f));
        var intensity = Math.Clamp((progress - 0.72f) / 0.28f, 0f, 1f);
        var radius = MathHelper.Lerp(Game1.tileSize * 0.7f, Game1.tileSize * 0.2f, intensity);
        var alpha = (1f - intensity) * 0.8f;
        b.Draw(pixel, new Rectangle((int)(center.X - radius), (int)(center.Y - radius), (int)(radius * 2f), (int)(radius * 2f)), null,
            Color.White * alpha, 0f, Vector2.Zero, SpriteEffects.None, 0.995f);

        for (var i = 0; i < 4; i++)
        {
            var angle = (float)Math.PI / 2f * i;
            var direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            DrawLine(b, pixel, center + direction * radius * 0.55f, center + direction * radius * 1.3f, 3f, Color.White * alpha, 0.996f);
        }
    }

    private static void DrawLine(SpriteBatch b, Texture2D pixel, Vector2 start, Vector2 end, float thickness, Color color, float layerDepth)
    {
        var delta = end - start;
        var length = delta.Length();
        if (length <= 0f)
            return;
        var angle = (float)Math.Atan2(delta.Y, delta.X);
        b.Draw(pixel, new Rectangle((int)start.X, (int)start.Y, (int)length, (int)Math.Max(1f, thickness)), null,
            color, angle, Vector2.Zero, SpriteEffects.None, layerDepth);
    }

    private Texture2D GetPixel()
    {
        if (_pixel is null)
        {
            _pixel = new Texture2D(Game1.graphics.GraphicsDevice, 1, 1);
            _pixel.SetData(new[] { Color.White });
        }
        return _pixel;
    }
}
