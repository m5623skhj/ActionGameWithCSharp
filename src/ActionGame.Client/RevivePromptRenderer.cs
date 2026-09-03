using ActionGame.Contracts.Protocol;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ActionGame.Client;

internal sealed class RevivePromptRenderer(Texture2D[] messages) : IDisposable
{
    private const int TopMargin = 24;
    private const int HorizontalPadding = 14;
    private const int VerticalPadding = 8;

    public static RevivePromptRenderer Load(GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        var textures = new List<Texture2D>();
        try
        {
            for (var seconds = 0;
                seconds <= GameProtocol.ReviveDelaySeconds;
                seconds++)
            {
                var path = Path.Combine(
                    AppContext.BaseDirectory,
                    "Assets",
                    "Ui",
                    "Revive",
                    $"revive_{seconds}.png");
                using var stream = File.OpenRead(path);
                textures.Add(Texture2D.FromStream(graphicsDevice, stream));
            }

            return new RevivePromptRenderer([.. textures]);
        }
        catch
        {
            foreach (var texture in textures)
            {
                texture.Dispose();
            }

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
        if (!localPlayer.IsDead)
        {
            return;
        }

        var messageIndex = Math.Min(
            localPlayer.ReviveSecondsRemaining,
            (byte)(messages.Length - 1));
        var message = messages[messageIndex];
        var x = ((int)GameProtocol.WorldWidth - message.Width) / 2;
        var background = new Rectangle(
            x - HorizontalPadding,
            TopMargin - VerticalPadding,
            message.Width + (2 * HorizontalPadding),
            message.Height + (2 * VerticalPadding));

        spriteBatch.Draw(pixel, background, new Color(10, 12, 16, 210));
        spriteBatch.Draw(message, new Vector2(x, TopMargin), Color.White);
    }

    public void Dispose()
    {
        foreach (var message in messages)
        {
            message.Dispose();
        }
    }
}
