using ActionGame.Contracts.Protocol;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ActionGame.Client;

internal sealed class HitVisualEffectRenderer
{
    private const float FlashDuration = 0.15f;
    private const float ImpactDuration = 0.20f;
    private const float FlashInterval = 0.035f;
    private const float InvulnerabilityFlashInterval = 0.06f;
    private static readonly Vector2[] BurstDirections =
    [
        new(1f, 0f),
        new(-1f, 0f),
        new(0f, 1f),
        new(0f, -1f),
        Vector2.Normalize(new Vector2(1f, 1f)),
        Vector2.Normalize(new Vector2(-1f, 1f)),
        Vector2.Normalize(new Vector2(1f, -1f)),
        Vector2.Normalize(new Vector2(-1f, -1f)),
    ];

    private readonly Dictionary<int, int> lastHealthByPlayerId = [];
    private readonly Dictionary<int, HitEffect> effectsByPlayerId = [];
    private float totalElapsedSeconds;

    public void ApplySnapshot(IReadOnlyList<PlayerSnapshot> players)
    {
        ArgumentNullException.ThrowIfNull(players);

        var activePlayerIds = new HashSet<int>();
        foreach (var player in players)
        {
            activePlayerIds.Add(player.PlayerId);
            if (lastHealthByPlayerId.TryGetValue(player.PlayerId, out var previousHealth)
                && player.Health < previousHealth)
            {
                effectsByPlayerId[player.PlayerId] = new HitEffect(
                    new Vector2(
                        player.X,
                        player.Y - player.Z - GameProtocol.PlayerSize),
                    previousHealth - player.Health);
            }

            lastHealthByPlayerId[player.PlayerId] = player.Health;
        }

        foreach (var playerId in lastHealthByPlayerId.Keys
            .Where(playerId => !activePlayerIds.Contains(playerId))
            .ToArray())
        {
            lastHealthByPlayerId.Remove(playerId);
            effectsByPlayerId.Remove(playerId);
        }
    }

    public void Update(float elapsedSeconds)
    {
        if (!float.IsFinite(elapsedSeconds) || elapsedSeconds < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        }

        totalElapsedSeconds += elapsedSeconds;
        foreach (var effect in effectsByPlayerId.Values)
        {
            effect.ElapsedSeconds += elapsedSeconds;
        }

        foreach (var playerId in effectsByPlayerId
            .Where(pair => pair.Value.ElapsedSeconds >= ImpactDuration)
            .Select(pair => pair.Key)
            .ToArray())
        {
            effectsByPlayerId.Remove(playerId);
        }
    }

    public Color GetCharacterTint(PlayerSnapshot player)
    {
        var tint = Color.White;
        if (effectsByPlayerId.TryGetValue(player.PlayerId, out var effect)
            && effect.ElapsedSeconds < FlashDuration)
        {
            var flashIndex = (int)(effect.ElapsedSeconds / FlashInterval);
            tint = flashIndex % 2 == 0
                ? new Color(255, 105, 105)
                : Color.White;
        }

        if (player.IsInvulnerable)
        {
            var invulnerabilityFlashIndex = (int)(
                totalElapsedSeconds / InvulnerabilityFlashInterval);
            if (invulnerabilityFlashIndex % 2 != 0)
            {
                tint *= 0.35f;
            }
        }

        return tint;
    }

    public void Draw(SpriteBatch spriteBatch, Texture2D pixel)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch);
        ArgumentNullException.ThrowIfNull(pixel);

        foreach (var effect in effectsByPlayerId.Values)
        {
            DrawImpact(spriteBatch, pixel, effect);
        }
    }

    public void Reset()
    {
        lastHealthByPlayerId.Clear();
        effectsByPlayerId.Clear();
        totalElapsedSeconds = 0f;
    }

    private static void DrawImpact(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        HitEffect effect)
    {
        var progress = effect.ElapsedSeconds / ImpactDuration;
        var isMeleeHit = effect.Damage == GameProtocol.AttackDamage
            || effect.Damage == GameProtocol.MeleeSkillDamage;
        var isSkillHit = effect.Damage == GameProtocol.MeleeSkillDamage
            || effect.Damage == GameProtocol.RangerSkillDamage;
        var radius = MathHelper.Lerp(
            isMeleeHit ? 5f : 4f,
            isSkillHit ? 24f : isMeleeHit ? 20f : 14f,
            progress);
        var color = (effect.Damage == GameProtocol.RangerSkillDamage
            ? new Color(95, 220, 255)
            : isMeleeHit
                ? new Color(255, 220, 90)
                : new Color(195, 225, 170)) * (1f - progress);
        var directionCount = isMeleeHit ? BurstDirections.Length : 4;
        for (var index = 0; index < directionCount; index++)
        {
            var direction = BurstDirections[index];
            var start = effect.Position + (direction * radius * 0.35f);
            var end = effect.Position + (direction * radius);
            DrawLine(spriteBatch, pixel, start, end, color, isMeleeHit ? 2f : 1f);
        }
    }

    private static void DrawLine(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        Vector2 start,
        Vector2 end,
        Color color,
        float thickness)
    {
        var delta = end - start;
        spriteBatch.Draw(
            pixel,
            start,
            sourceRectangle: null,
            color,
            MathF.Atan2(delta.Y, delta.X),
            Vector2.Zero,
            new Vector2(delta.Length(), thickness),
            SpriteEffects.None,
            layerDepth: 0f);
    }

    private sealed class HitEffect(Vector2 position, int damage)
    {
        public Vector2 Position { get; } = position;

        public int Damage { get; } = damage;

        public float ElapsedSeconds { get; set; }
    }
}
