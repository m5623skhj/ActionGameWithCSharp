using ActionGame.Contracts.Protocol;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ActionGame.Client;

internal sealed class SkillBarRenderer : IDisposable
{
    private const int SlotCount = 6;
    private const int SlotSize = 48;
    private const int SlotGap = 4;
    private const int PanelPadding = 6;
    private const int ScreenMargin = 18;
    private const int IconInset = 4;
    private const int IconSize = SlotSize - (2 * IconInset);
    private const int PanelWidth = (2 * PanelPadding)
        + (SlotCount * SlotSize)
        + ((SlotCount - 1) * SlotGap);
    private const int PanelHeight = SlotSize + (2 * PanelPadding);
    private const int DigitScale = 3;
    private static readonly byte[][] DigitGlyphs =
    [
        [0b111, 0b101, 0b101, 0b101, 0b111],
        [0b010, 0b110, 0b010, 0b010, 0b111],
        [0b111, 0b001, 0b111, 0b100, 0b111],
        [0b111, 0b001, 0b111, 0b001, 0b111],
        [0b101, 0b101, 0b111, 0b001, 0b001],
        [0b111, 0b100, 0b111, 0b001, 0b111],
        [0b111, 0b100, 0b111, 0b101, 0b111],
        [0b111, 0b001, 0b010, 0b010, 0b010],
        [0b111, 0b101, 0b111, 0b101, 0b111],
        [0b111, 0b101, 0b111, 0b001, 0b111],
    ];

    private readonly Texture2D warriorSkillIcon;
    private readonly Texture2D rangerSkillIcon;
    private readonly Texture2D cooldownMask;
    private readonly Color[] cooldownMaskPixels = new Color[IconSize * IconSize];
    private int displayedCooldownTenths = -1;

    private SkillBarRenderer(
        Texture2D warriorSkillIcon,
        Texture2D rangerSkillIcon,
        Texture2D cooldownMask)
    {
        this.warriorSkillIcon = warriorSkillIcon;
        this.rangerSkillIcon = rangerSkillIcon;
        this.cooldownMask = cooldownMask;
    }

    public static SkillBarRenderer Load(GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        Texture2D? warriorSkillIcon = null;
        Texture2D? rangerSkillIcon = null;
        Texture2D? cooldownMask = null;
        try
        {
            warriorSkillIcon = LoadTexture(graphicsDevice, "warrior_dash_slash.png");
            rangerSkillIcon = LoadTexture(graphicsDevice, "ranger_power_arrow.png");
            cooldownMask = new Texture2D(graphicsDevice, IconSize, IconSize);
            return new SkillBarRenderer(
                warriorSkillIcon,
                rangerSkillIcon,
                cooldownMask);
        }
        catch
        {
            warriorSkillIcon?.Dispose();
            rangerSkillIcon?.Dispose();
            cooldownMask?.Dispose();
            throw;
        }
    }

    public void Draw(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        PlayerSnapshot localPlayer)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch);
        ArgumentNullException.ThrowIfNull(pixel);
        if (localPlayer.IsDead)
        {
            return;
        }

        var viewport = spriteBatch.GraphicsDevice.Viewport;
        var panelX = viewport.Width - ScreenMargin - PanelWidth;
        var panelY = viewport.Height - ScreenMargin - PanelHeight;
        DrawPanel(spriteBatch, pixel, panelX, panelY);

        for (var index = 0; index < SlotCount; index++)
        {
            var slotX = panelX + PanelPadding + (index * (SlotSize + SlotGap));
            var slotY = panelY + PanelPadding;
            DrawSlot(spriteBatch, pixel, slotX, slotY);
            if (index == 0)
            {
                DrawSkill(spriteBatch, pixel, localPlayer, slotX, slotY);
            }
        }
    }

    public void Dispose()
    {
        warriorSkillIcon.Dispose();
        rangerSkillIcon.Dispose();
        cooldownMask.Dispose();
    }

    private static Texture2D LoadTexture(
        GraphicsDevice graphicsDevice,
        string fileName)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Ui",
            "Skills",
            fileName);
        using var stream = File.OpenRead(path);
        return Texture2D.FromStream(graphicsDevice, stream);
    }

    private static void DrawPanel(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        int x,
        int y)
    {
        spriteBatch.Draw(
            pixel,
            new Rectangle(x, y, PanelWidth, PanelHeight),
            new Color(9, 12, 16, 230));
        DrawBorder(
            spriteBatch,
            pixel,
            new Rectangle(x, y, PanelWidth, PanelHeight),
            new Color(113, 91, 52));
        spriteBatch.Draw(
            pixel,
            new Rectangle(x + 8, y + 2, PanelWidth - 16, 2),
            new Color(42, 174, 181));
        spriteBatch.Draw(
            pixel,
            new Rectangle(x + 8, y + PanelHeight - 4, PanelWidth - 16, 2),
            new Color(24, 89, 99));
    }

    private static void DrawSlot(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        int x,
        int y)
    {
        var bounds = new Rectangle(x, y, SlotSize, SlotSize);
        spriteBatch.Draw(pixel, bounds, new Color(15, 18, 22, 245));
        DrawBorder(spriteBatch, pixel, bounds, new Color(99, 93, 77));
        spriteBatch.Draw(
            pixel,
            new Rectangle(x + 3, y + 3, SlotSize - 6, SlotSize - 6),
            new Color(7, 10, 13, 235));
        DrawCorner(spriteBatch, pixel, x + 2, y + 2);
        DrawCorner(spriteBatch, pixel, x + SlotSize - 5, y + 2);
        DrawCorner(spriteBatch, pixel, x + 2, y + SlotSize - 5);
        DrawCorner(spriteBatch, pixel, x + SlotSize - 5, y + SlotSize - 5);
    }

    private void DrawSkill(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        PlayerSnapshot player,
        int slotX,
        int slotY)
    {
        var iconBounds = new Rectangle(
            slotX + IconInset,
            slotY + IconInset,
            IconSize,
            IconSize);
        var icon = player.PlayerId == GameProtocol.RangerPlayerId
            ? rangerSkillIcon
            : warriorSkillIcon;
        var isReady = player.SkillCooldownRemaining <= 0f;
        if (isReady)
        {
            var glowColor = player.PlayerId == GameProtocol.RangerPlayerId
                ? new Color(57, 218, 225)
                : new Color(245, 153, 42);
            DrawBorder(
                spriteBatch,
                pixel,
                new Rectangle(slotX + 1, slotY + 1, SlotSize - 2, SlotSize - 2),
                glowColor);
        }

        spriteBatch.Draw(icon, iconBounds, Color.White);
        if (isReady)
        {
            return;
        }

        var cooldownTenths = Math.Max(
            1,
            (int)MathF.Ceiling(player.SkillCooldownRemaining * 10f));
        UpdateCooldownMask(cooldownTenths);
        spriteBatch.Draw(cooldownMask, iconBounds, Color.White);
        DrawCooldownNumber(
            spriteBatch,
            pixel,
            iconBounds,
            cooldownTenths);
    }

    private void UpdateCooldownMask(int cooldownTenths)
    {
        if (displayedCooldownTenths == cooldownTenths)
        {
            return;
        }

        displayedCooldownTenths = cooldownTenths;
        var remainingRatio = Math.Clamp(
            cooldownTenths / (GameProtocol.SkillCooldown * 10f),
            0f,
            1f);
        var center = IconSize / 2f;
        for (var y = 0; y < IconSize; y++)
        {
            for (var x = 0; x < IconSize; x++)
            {
                var deltaX = (x + 0.5f) - center;
                var deltaY = (y + 0.5f) - center;
                var angle = MathF.Atan2(deltaX, -deltaY);
                if (angle < 0f)
                {
                    angle += MathF.Tau;
                }

                var clockwiseRatio = angle / MathF.Tau;
                cooldownMaskPixels[(y * IconSize) + x] =
                    clockwiseRatio < remainingRatio
                        ? new Color(4, 7, 11, 205)
                        : Color.Transparent;
            }
        }

        cooldownMask.SetData(cooldownMaskPixels);
    }

    private static void DrawCooldownNumber(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        Rectangle iconBounds,
        int cooldownTenths)
    {
        const int glyphWidth = 3 * DigitScale;
        const int dotWidth = DigitScale;
        const int spacing = DigitScale;
        const int textWidth = (2 * glyphWidth) + dotWidth + (2 * spacing);
        const int textHeight = 5 * DigitScale;
        var x = iconBounds.X + ((iconBounds.Width - textWidth) / 2);
        var y = iconBounds.Y + ((iconBounds.Height - textHeight) / 2);
        var seconds = Math.Clamp(cooldownTenths / 10, 0, 9);
        var tenths = cooldownTenths % 10;

        DrawNumber(spriteBatch, pixel, x + 1, y + 1, seconds, new Color(0, 0, 0, 220));
        DrawDot(
            spriteBatch,
            pixel,
            x + glyphWidth + spacing + 1,
            y + 1,
            new Color(0, 0, 0, 220));
        DrawNumber(
            spriteBatch,
            pixel,
            x + glyphWidth + dotWidth + (2 * spacing) + 1,
            y + 1,
            tenths,
            new Color(0, 0, 0, 220));

        DrawNumber(spriteBatch, pixel, x, y, seconds, Color.White);
        DrawDot(
            spriteBatch,
            pixel,
            x + glyphWidth + spacing,
            y,
            Color.White);
        DrawNumber(
            spriteBatch,
            pixel,
            x + glyphWidth + dotWidth + (2 * spacing),
            y,
            tenths,
            Color.White);
    }

    private static void DrawNumber(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        int x,
        int y,
        int number,
        Color color)
    {
        var rows = DigitGlyphs[number];
        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                if ((rows[row] & (1 << (2 - column))) == 0)
                {
                    continue;
                }

                spriteBatch.Draw(
                    pixel,
                    new Rectangle(
                        x + (column * DigitScale),
                        y + (row * DigitScale),
                        DigitScale,
                        DigitScale),
                    color);
            }
        }
    }

    private static void DrawDot(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        int x,
        int y,
        Color color)
    {
        spriteBatch.Draw(
            pixel,
            new Rectangle(x, y + (4 * DigitScale), DigitScale, DigitScale),
            color);
    }

    private static void DrawBorder(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        Rectangle bounds,
        Color color)
    {
        spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, 2), color);
        spriteBatch.Draw(
            pixel,
            new Rectangle(bounds.X, bounds.Bottom - 2, bounds.Width, 2),
            color);
        spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Y, 2, bounds.Height), color);
        spriteBatch.Draw(
            pixel,
            new Rectangle(bounds.Right - 2, bounds.Y, 2, bounds.Height),
            color);
    }

    private static void DrawCorner(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        int x,
        int y)
    {
        spriteBatch.Draw(pixel, new Rectangle(x, y, 3, 3), new Color(139, 111, 61));
    }
}
