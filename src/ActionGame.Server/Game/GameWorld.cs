using ActionGame.Contracts.Protocol;

namespace ActionGame.Server.Game;

public sealed class GameWorld
{
    private readonly Dictionary<long, PlayerState> playersByConnection = [];

    public int PlayerCount => playersByConnection.Count;

    public long ServerTick { get; private set; }

    public bool TryJoin(
        long connectionId,
        out int playerId,
        out JoinRejectReason rejectionReason)
    {
        if (playersByConnection.TryGetValue(connectionId, out var existingPlayer))
        {
            playerId = existingPlayer.PlayerId;
            rejectionReason = JoinRejectReason.AlreadyJoined;
            return false;
        }

        if (playersByConnection.Count >= GameProtocol.MaxPlayers)
        {
            playerId = 0;
            rejectionReason = JoinRejectReason.ServerFull;
            return false;
        }

        playerId = FindAvailablePlayerId();
        var spawnPosition = GetSpawnPosition(playerId);
        playersByConnection.Add(
            connectionId,
            new PlayerState(playerId, spawnPosition.X, spawnPosition.Y));
        rejectionReason = default;
        return true;
    }

    public bool Leave(long connectionId)
    {
        return playersByConnection.Remove(connectionId);
    }

    public bool ApplyInput(long connectionId, InputCommandPacket input)
    {
        if (input.Horizontal is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        if (input.Depth is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        const InputActionFlags validActions = InputActionFlags.Attack
            | InputActionFlags.Jump
            | InputActionFlags.ReservedZ;
        if ((input.Actions & ~validActions) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        if (!playersByConnection.TryGetValue(connectionId, out var player))
        {
            return false;
        }

        if (player.HasInput && input.Sequence <= player.LastInputSequence)
        {
            return false;
        }

        player.HasInput = true;
        player.LastInputSequence = input.Sequence;
        if (player.IsDead)
        {
            ClearInput(player);
            return true;
        }

        player.Horizontal = input.Horizontal;
        player.Depth = input.Depth;
        player.PendingAttack |= IsPressed(
            input.Actions,
            player.HeldActions,
            InputActionFlags.Attack);
        player.PendingJump |= IsPressed(
            input.Actions,
            player.HeldActions,
            InputActionFlags.Jump);
        player.HeldActions = input.Actions;
        return true;
    }

    public void Update(float deltaSeconds)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        var halfPlayerSize = GameProtocol.PlayerSize / 2f;
        var attackers = new List<PlayerState>();
        foreach (var player in playersByConnection.Values)
        {
            player.AttackTimeRemaining = Math.Max(
                0f,
                player.AttackTimeRemaining - deltaSeconds);
            player.AttackCooldownRemaining = Math.Max(
                0f,
                player.AttackCooldownRemaining - deltaSeconds);
            if (player.IsDead)
            {
                ClearInput(player);
                player.AttackTimeRemaining = 0f;
                player.AttackCooldownRemaining = 0f;
                player.VerticalVelocity = 0f;
                player.Z = 0f;
                continue;
            }

            if (player.PendingJump && player.Z <= 0f)
            {
                player.VerticalVelocity = GameProtocol.JumpInitialVelocity;
            }

            if (player.PendingAttack && player.AttackCooldownRemaining <= 0f)
            {
                player.AttackTimeRemaining = GameProtocol.AttackDuration;
                player.AttackCooldownRemaining = GameProtocol.AttackCooldown;
                attackers.Add(player);
            }

            player.PendingJump = false;
            player.PendingAttack = false;

            var horizontal = (float)player.Horizontal;
            var depth = (float)player.Depth;
            var lengthSquared = (horizontal * horizontal) + (depth * depth);
            if (lengthSquared > 1f)
            {
                var inverseLength = 1f / MathF.Sqrt(lengthSquared);
                horizontal *= inverseLength;
                depth *= inverseLength;
            }

            if (horizontal < 0f)
            {
                player.Facing = FacingDirection.Left;
            }
            else if (horizontal > 0f)
            {
                player.Facing = FacingDirection.Right;
            }

            player.X = Math.Clamp(
                player.X + (horizontal * GameProtocol.HorizontalSpeed * deltaSeconds),
                halfPlayerSize,
                GameProtocol.WorldWidth - halfPlayerSize);
            player.Y = Math.Clamp(
                player.Y + (depth * GameProtocol.DepthSpeed * deltaSeconds),
                GameProtocol.FloorTop,
                GameProtocol.FloorBottom);

            if (player.Z > 0f || player.VerticalVelocity > 0f)
            {
                player.Z += player.VerticalVelocity * deltaSeconds;
                player.VerticalVelocity -= GameProtocol.Gravity * deltaSeconds;
                if (player.Z <= 0f)
                {
                    player.Z = 0f;
                    player.VerticalVelocity = 0f;
                }
            }
        }

        ResolveAttacks(attackers);

        ServerTick++;
    }

    public WorldSnapshotPacket CreateSnapshot()
    {
        var players = playersByConnection.Values
            .OrderBy(player => player.PlayerId)
            .Select(player => new PlayerSnapshot(
                player.PlayerId,
                player.X,
                player.Y,
                player.Z,
                player.Facing,
                player.AttackTimeRemaining > 0f,
                player.Health,
                player.IsDead))
            .ToArray();
        return new WorldSnapshotPacket(ServerTick, players);
    }

    public bool TryGetPlayer(long connectionId, out PlayerSnapshot player)
    {
        if (playersByConnection.TryGetValue(connectionId, out var state))
        {
            player = new PlayerSnapshot(
                state.PlayerId,
                state.X,
                state.Y,
                state.Z,
                state.Facing,
                state.AttackTimeRemaining > 0f,
                state.Health,
                state.IsDead);
            return true;
        }

        player = default;
        return false;
    }

    private int FindAvailablePlayerId()
    {
        for (var playerId = 1; playerId <= GameProtocol.MaxPlayers; playerId++)
        {
            if (playersByConnection.Values.All(player => player.PlayerId != playerId))
            {
                return playerId;
            }
        }

        throw new InvalidOperationException("No player slot is available.");
    }

    private static (float X, float Y) GetSpawnPosition(int playerId)
    {
        var middleDepth = (GameProtocol.FloorTop + GameProtocol.FloorBottom) / 2f;
        return playerId switch
        {
            1 => (100f, middleDepth),
            2 => (GameProtocol.WorldWidth - 100f, middleDepth),
            _ => throw new ArgumentOutOfRangeException(nameof(playerId)),
        };
    }

    private static bool IsPressed(
        InputActionFlags current,
        InputActionFlags previous,
        InputActionFlags action)
    {
        return (current & action) != 0 && (previous & action) == 0;
    }

    private void ResolveAttacks(IReadOnlyList<PlayerState> attackers)
    {
        var pendingDamage = new Dictionary<PlayerState, int>();
        foreach (var attacker in attackers)
        {
            foreach (var target in playersByConnection.Values)
            {
                if (ReferenceEquals(attacker, target)
                    || target.IsDead
                    || !IsWithinAttackRange(attacker, target))
                {
                    continue;
                }

                pendingDamage.TryGetValue(target, out var damage);
                pendingDamage[target] = damage + GameProtocol.AttackDamage;
            }
        }

        foreach (var pair in pendingDamage)
        {
            ApplyDamage(pair.Key, pair.Value);
        }
    }

    private static bool IsWithinAttackRange(PlayerState attacker, PlayerState target)
    {
        var facingMultiplier = attacker.Facing == FacingDirection.Right ? 1f : -1f;
        var forwardDistance = (target.X - attacker.X) * facingMultiplier;
        var horizontalReach = GameProtocol.PlayerSize + GameProtocol.AttackReach;
        return forwardDistance >= 0f
            && forwardDistance <= horizontalReach
            && MathF.Abs(target.Y - attacker.Y) <= GameProtocol.AttackDepthTolerance
            && MathF.Abs(target.Z - attacker.Z) <= GameProtocol.AttackHeightTolerance;
    }

    private static void ApplyDamage(PlayerState player, int damage)
    {
        player.Health = Math.Max(0, player.Health - damage);
        if (!player.IsDead)
        {
            return;
        }

        ClearInput(player);
        player.AttackTimeRemaining = 0f;
        player.AttackCooldownRemaining = 0f;
        player.VerticalVelocity = 0f;
        player.Z = 0f;
    }

    private static void ClearInput(PlayerState player)
    {
        player.Horizontal = 0;
        player.Depth = 0;
        player.HeldActions = InputActionFlags.None;
        player.PendingAttack = false;
        player.PendingJump = false;
    }

    private sealed class PlayerState(int playerId, float x, float y)
    {
        public int PlayerId { get; } = playerId;

        public float X { get; set; } = x;

        public float Y { get; set; } = y;

        public float Z { get; set; }

        public sbyte Horizontal { get; set; }

        public sbyte Depth { get; set; }

        public float VerticalVelocity { get; set; }

        public float AttackTimeRemaining { get; set; }

        public float AttackCooldownRemaining { get; set; }

        public int Health { get; set; } = GameProtocol.MaxHealth;

        public bool IsDead => Health == 0;

        public FacingDirection Facing { get; set; } = FacingDirection.Right;

        public InputActionFlags HeldActions { get; set; }

        public bool PendingAttack { get; set; }

        public bool PendingJump { get; set; }

        public uint LastInputSequence { get; set; }

        public bool HasInput { get; set; }
    }
}
