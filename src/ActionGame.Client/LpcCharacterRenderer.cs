using ActionGame.Contracts.Protocol;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ActionGame.Client;

internal sealed class LpcCharacterRenderer : IDisposable
{
    private const int StandardFrameSize = 64;
    private const float MovementEpsilon = 0.01f;
    private const float WalkFrameDuration = 0.09f;
    private const float IdleFrameDuration = 0.30f;
    private const float JumpFrameDuration = 0.12f;
    private const float DeathFrameDuration = 0.08f;
    private const int DeathFrameCount = 6;
    private static readonly int[] IdleFrames = [0, 0, 1];
    private static readonly int[] JumpFrames = [0, 1, 2, 3, 4, 1];
    private static readonly Vector2 StandardOrigin = new(32f, 56f);
    private static readonly Vector2 OversizedAttackOrigin =
        StandardOrigin + new Vector2(32f, 32f);

    private readonly Dictionary<int, PlayerAnimationState> playerStates = [];
    private readonly CharacterSprites warrior;
    private readonly CharacterSprites ranger;

    private LpcCharacterRenderer(CharacterSprites warrior, CharacterSprites ranger)
    {
        this.warrior = warrior;
        this.ranger = ranger;
    }

    public static LpcCharacterRenderer Load(GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        var warrior = CharacterSprites.Load(
            graphicsDevice,
            "Warrior",
            attackFrameSize: 128,
            attackFrameCount: 6,
            attackOrigin: OversizedAttackOrigin);
        try
        {
            var ranger = CharacterSprites.Load(
                graphicsDevice,
                "Ranger",
                attackFrameSize: StandardFrameSize,
                attackFrameCount: 13,
                attackOrigin: StandardOrigin);
            return new LpcCharacterRenderer(warrior, ranger);
        }
        catch
        {
            warrior.Dispose();
            throw;
        }
    }

    public void ApplySnapshot(IReadOnlyList<PlayerSnapshot> players)
    {
        ArgumentNullException.ThrowIfNull(players);

        var activePlayerIds = new HashSet<int>();
        foreach (var player in players)
        {
            activePlayerIds.Add(player.PlayerId);
            if (!playerStates.TryGetValue(player.PlayerId, out var state))
            {
                state = new PlayerAnimationState(player);
                playerStates.Add(player.PlayerId, state);
            }
            else
            {
                state.Apply(player);
            }
        }

        foreach (var playerId in playerStates.Keys
            .Where(playerId => !activePlayerIds.Contains(playerId))
            .ToArray())
        {
            playerStates.Remove(playerId);
        }
    }

    public void Update(float elapsedSeconds)
    {
        if (!float.IsFinite(elapsedSeconds) || elapsedSeconds < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        }

        foreach (var state in playerStates.Values)
        {
            state.ElapsedSeconds += elapsedSeconds;
        }
    }

    public void Draw(SpriteBatch spriteBatch, PlayerSnapshot player, Color tint)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch);

        if (!playerStates.TryGetValue(player.PlayerId, out var state))
        {
            state = new PlayerAnimationState(player);
            playerStates.Add(player.PlayerId, state);
        }

        var sprites = player.PlayerId == 1 ? warrior : ranger;
        var texture = sprites.GetTexture(state.Animation);
        var frameSize = state.Animation == CharacterAnimation.Attack
            ? sprites.AttackFrameSize
            : StandardFrameSize;
        var frameIndex = GetFrameIndex(state, sprites.AttackFrameCount);
        var direction = state.Animation == CharacterAnimation.Attack
            ? DirectionFromFacing(player.Facing)
            : state.Animation == CharacterAnimation.Death
                ? CharacterDirection.Up
                : state.Direction;
        var sourceRectangle = new Rectangle(
            frameIndex * frameSize,
            (int)direction * frameSize,
            frameSize,
            frameSize);
        var origin = state.Animation == CharacterAnimation.Attack
            ? sprites.AttackOrigin
            : StandardOrigin;
        var groundPosition = new Vector2(player.X, player.Y - player.Z);

        spriteBatch.Draw(
            texture,
            groundPosition,
            sourceRectangle,
            tint,
            0f,
            origin,
            1f,
            SpriteEffects.None,
            0f);
    }

    public void Reset()
    {
        playerStates.Clear();
    }

    public void Dispose()
    {
        warrior.Dispose();
        ranger.Dispose();
    }

    private static int GetFrameIndex(PlayerAnimationState state, int attackFrameCount)
    {
        return state.Animation switch
        {
            CharacterAnimation.Idle => GetLoopingFrame(
                IdleFrames,
                state.ElapsedSeconds,
                IdleFrameDuration),
            CharacterAnimation.Walk => 1
                + ((int)(state.ElapsedSeconds / WalkFrameDuration) % 8),
            CharacterAnimation.Jump => GetClampedFrame(
                JumpFrames,
                state.ElapsedSeconds,
                JumpFrameDuration),
            CharacterAnimation.Attack => Math.Min(
                attackFrameCount - 1,
                (int)(state.ElapsedSeconds / GameProtocol.AttackDuration
                    * attackFrameCount)),
            CharacterAnimation.Death => Math.Min(
                DeathFrameCount - 1,
                (int)(state.ElapsedSeconds / DeathFrameDuration)),
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
    }

    private static int GetLoopingFrame(
        IReadOnlyList<int> frames,
        float elapsedSeconds,
        float frameDuration)
    {
        var index = (int)(elapsedSeconds / frameDuration) % frames.Count;
        return frames[index];
    }

    private static int GetClampedFrame(
        IReadOnlyList<int> frames,
        float elapsedSeconds,
        float frameDuration)
    {
        var index = Math.Min(frames.Count - 1, (int)(elapsedSeconds / frameDuration));
        return frames[index];
    }

    private static CharacterDirection DirectionFromFacing(FacingDirection facing)
    {
        return facing == FacingDirection.Left
            ? CharacterDirection.Left
            : CharacterDirection.Right;
    }

    private static CharacterDirection DirectionFromMovement(
        float deltaX,
        float deltaY,
        FacingDirection facing)
    {
        if (MathF.Abs(deltaY) > MathF.Abs(deltaX))
        {
            return deltaY < 0f ? CharacterDirection.Up : CharacterDirection.Down;
        }

        if (MathF.Abs(deltaX) > MovementEpsilon)
        {
            return deltaX < 0f ? CharacterDirection.Left : CharacterDirection.Right;
        }

        return DirectionFromFacing(facing);
    }

    private enum CharacterAnimation
    {
        Idle,
        Walk,
        Jump,
        Attack,
        Death,
    }

    private enum CharacterDirection
    {
        Up,
        Left,
        Down,
        Right,
    }

    private sealed class PlayerAnimationState
    {
        public PlayerAnimationState(PlayerSnapshot player)
        {
            LastX = player.X;
            LastY = player.Y;
            Direction = DirectionFromFacing(player.Facing);
            Animation = GetAnimation(player, isMoving: false);
        }

        public float LastX { get; private set; }

        public float LastY { get; private set; }

        public float ElapsedSeconds { get; set; }

        public CharacterDirection Direction { get; private set; }

        public CharacterAnimation Animation { get; private set; }

        public void Apply(PlayerSnapshot player)
        {
            var deltaX = player.X - LastX;
            var deltaY = player.Y - LastY;
            var isMoving = MathF.Abs(deltaX) > MovementEpsilon
                || MathF.Abs(deltaY) > MovementEpsilon;
            if (isMoving)
            {
                Direction = DirectionFromMovement(deltaX, deltaY, player.Facing);
            }

            var nextAnimation = GetAnimation(player, isMoving);
            if (nextAnimation != Animation)
            {
                Animation = nextAnimation;
                ElapsedSeconds = 0f;
            }

            LastX = player.X;
            LastY = player.Y;
        }

        private static CharacterAnimation GetAnimation(
            PlayerSnapshot player,
            bool isMoving)
        {
            if (player.IsDead)
            {
                return CharacterAnimation.Death;
            }

            if (player.IsAttacking)
            {
                return CharacterAnimation.Attack;
            }

            if (player.Z > 0f)
            {
                return CharacterAnimation.Jump;
            }

            return isMoving ? CharacterAnimation.Walk : CharacterAnimation.Idle;
        }
    }

    private sealed class CharacterSprites(
        Texture2D idle,
        Texture2D walk,
        Texture2D jump,
        Texture2D attack,
        Texture2D death,
        int attackFrameSize,
        int attackFrameCount,
        Vector2 attackOrigin) : IDisposable
    {
        public int AttackFrameSize { get; } = attackFrameSize;

        public int AttackFrameCount { get; } = attackFrameCount;

        public Vector2 AttackOrigin { get; } = attackOrigin;

        public static CharacterSprites Load(
            GraphicsDevice graphicsDevice,
            string characterName,
            int attackFrameSize,
            int attackFrameCount,
            Vector2 attackOrigin)
        {
            var textures = new List<Texture2D>();
            try
            {
                var idle = LoadTexture(graphicsDevice, characterName, "idle.png");
                textures.Add(idle);
                var walk = LoadTexture(graphicsDevice, characterName, "walk.png");
                textures.Add(walk);
                var jump = LoadTexture(graphicsDevice, characterName, "jump.png");
                textures.Add(jump);
                var attack = LoadTexture(graphicsDevice, characterName, "attack.png");
                textures.Add(attack);
                var death = LoadTexture(graphicsDevice, characterName, "death.png");
                textures.Add(death);
                return new CharacterSprites(
                    idle,
                    walk,
                    jump,
                    attack,
                    death,
                    attackFrameSize,
                    attackFrameCount,
                    attackOrigin);
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

        public Texture2D GetTexture(CharacterAnimation animation)
        {
            return animation switch
            {
                CharacterAnimation.Idle => idle,
                CharacterAnimation.Walk => walk,
                CharacterAnimation.Jump => jump,
                CharacterAnimation.Attack => attack,
                CharacterAnimation.Death => death,
                _ => throw new ArgumentOutOfRangeException(nameof(animation)),
            };
        }

        public void Dispose()
        {
            idle.Dispose();
            walk.Dispose();
            jump.Dispose();
            attack.Dispose();
            death.Dispose();
        }

        private static Texture2D LoadTexture(
            GraphicsDevice graphicsDevice,
            string characterName,
            string fileName)
        {
            var path = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "Lpc",
                characterName,
                fileName);
            using var stream = File.OpenRead(path);
            return Texture2D.FromStream(graphicsDevice, stream);
        }
    }
}
