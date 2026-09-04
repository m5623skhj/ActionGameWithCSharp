using ActionGame.Contracts.Protocol;

namespace ActionGame.Server.Game;

public sealed class GameWorld
{
    private readonly Dictionary<long, PlayerState> playersByConnection = [];
    private readonly List<ArrowState> arrows = [];
    private double serverTimeSeconds;
    private int nextArrowId = 1;

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
        if (!playersByConnection.Remove(connectionId, out var player))
        {
            return false;
        }

        arrows.RemoveAll(arrow => arrow.OwnerPlayerId == player.PlayerId);
        return true;
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
            | InputActionFlags.ReservedZ
            | InputActionFlags.Revive;
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
            player.Horizontal = 0;
            player.Depth = 0;
            player.PendingAttack = false;
            player.PendingJump = false;
            player.PendingRevive |= IsPressed(
                input.Actions,
                player.HeldActions,
                InputActionFlags.Revive);
            player.HeldActions = input.Actions;
            return true;
        }

        player.Horizontal = input.Horizontal;
        player.Depth = input.Depth;
        var wasHoldingRevive =
            (player.HeldActions & InputActionFlags.Revive) != 0;
        player.PendingAttack |= !wasHoldingRevive
            && IsPressed(
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

        serverTimeSeconds += deltaSeconds;
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
                var reviveRequested = player.PendingRevive;
                player.PendingRevive = false;
                if (reviveRequested && CanRevive(player))
                {
                    Revive(player);
                    continue;
                }

                ClearControllableState(player, clearHeldActions: false);
                player.AttackTimeRemaining = 0f;
                player.AttackCooldownRemaining = 0f;
                player.VerticalVelocity = 0f;
                player.KnockbackVelocityX = 0f;
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

            var inputVelocityX = player.KnockbackVelocityX == 0f
                ? horizontal * GameProtocol.HorizontalSpeed
                : 0f;
            var nextX = player.X
                + ((inputVelocityX + player.KnockbackVelocityX) * deltaSeconds);
            player.X = Math.Clamp(
                nextX,
                halfPlayerSize,
                GameProtocol.WorldWidth - halfPlayerSize);
            if ((player.X <= halfPlayerSize && player.KnockbackVelocityX < 0f)
                || (player.X >= GameProtocol.WorldWidth - halfPlayerSize
                    && player.KnockbackVelocityX > 0f))
            {
                player.KnockbackVelocityX = 0f;
            }
            else
            {
                player.KnockbackVelocityX = MoveTowardsZero(
                    player.KnockbackVelocityX,
                    GameProtocol.KnockbackDeceleration * deltaSeconds);
            }

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
        UpdateArrows(deltaSeconds);

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
                player.IsDead,
                GetReviveSecondsRemaining(player)))
            .ToArray();
        var arrowSnapshots = arrows
            .OrderBy(arrow => arrow.ArrowId)
            .Select(arrow => new ArrowSnapshot(
                arrow.ArrowId,
                arrow.OwnerPlayerId,
                arrow.X,
                arrow.Y,
                arrow.Z,
                arrow.Direction))
            .ToArray();
        return new WorldSnapshotPacket(ServerTick, players, arrowSnapshots);
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
                state.IsDead,
                GetReviveSecondsRemaining(state));
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
        var pendingHits = new Dictionary<PlayerState, PendingHit>();
        foreach (var attacker in attackers)
        {
            if (attacker.PlayerId == GameProtocol.RangerPlayerId)
            {
                SpawnArrow(attacker);
                continue;
            }

            foreach (var target in playersByConnection.Values)
            {
                if (ReferenceEquals(attacker, target)
                    || target.IsDead
                    || !IsWithinAttackRange(attacker, target))
                {
                    continue;
                }

                pendingHits.TryGetValue(target, out var hit);
                var directionMultiplier = attacker.Facing == FacingDirection.Right
                    ? 1f
                    : -1f;
                pendingHits[target] = new PendingHit(
                    hit.Damage + GameProtocol.AttackDamage,
                    hit.KnockbackVelocityX
                        + (directionMultiplier * GameProtocol.MeleeKnockbackSpeed));
            }
        }

        foreach (var pair in pendingHits)
        {
            ApplyDamage(
                pair.Key,
                pair.Value.Damage,
                Math.Clamp(
                    pair.Value.KnockbackVelocityX,
                    -GameProtocol.MeleeKnockbackSpeed,
                    GameProtocol.MeleeKnockbackSpeed));
        }
    }

    private void SpawnArrow(PlayerState attacker)
    {
        if (arrows.Count >= GameProtocol.MaxArrows)
        {
            return;
        }

        var directionMultiplier = attacker.Facing == FacingDirection.Right ? 1f : -1f;
        arrows.Add(new ArrowState(
            nextArrowId++,
            attacker.PlayerId,
            attacker.X + (directionMultiplier * GameProtocol.PlayerSize / 2f),
            attacker.Y,
            attacker.Z + GameProtocol.ArrowSpawnHeight,
            attacker.Facing));
    }

    private void UpdateArrows(float deltaSeconds)
    {
        for (var index = arrows.Count - 1; index >= 0; index--)
        {
            var arrow = arrows[index];
            var remainingDistance = GameProtocol.ArrowMaxDistance - arrow.DistanceTraveled;
            if (remainingDistance <= 0f)
            {
                arrows.RemoveAt(index);
                continue;
            }

            var travelDistance = MathF.Min(
                GameProtocol.ArrowSpeed * deltaSeconds,
                remainingDistance);
            var directionMultiplier = arrow.Direction == FacingDirection.Right ? 1f : -1f;
            var previousX = arrow.X;
            var nextX = previousX + (directionMultiplier * travelDistance);
            var target = FindArrowTarget(arrow, previousX, nextX);
            if (target is not null)
            {
                var knockbackVelocityX = arrow.Direction == FacingDirection.Right
                    ? GameProtocol.ArrowKnockbackSpeed
                    : -GameProtocol.ArrowKnockbackSpeed;
                ApplyDamage(
                    target,
                    GameProtocol.RangerAttackDamage,
                    knockbackVelocityX);
                arrows.RemoveAt(index);
                continue;
            }

            arrow.X = nextX;
            arrow.DistanceTraveled += travelDistance;
            if (arrow.DistanceTraveled >= GameProtocol.ArrowMaxDistance
                || arrow.X < 0f
                || arrow.X > GameProtocol.WorldWidth)
            {
                arrows.RemoveAt(index);
            }
        }
    }

    private PlayerState? FindArrowTarget(
        ArrowState arrow,
        float previousX,
        float nextX)
    {
        PlayerState? closestTarget = null;
        var closestDistance = float.MaxValue;
        foreach (var target in playersByConnection.Values)
        {
            if (target.PlayerId == arrow.OwnerPlayerId
                || target.IsDead
                || MathF.Abs(target.Y - arrow.Y) > GameProtocol.ArrowDepthTolerance
                || MathF.Abs(
                    target.Z + GameProtocol.ArrowSpawnHeight - arrow.Z)
                    > GameProtocol.ArrowHeightTolerance
                || !TryGetArrowHitDistance(
                    previousX,
                    nextX,
                    target.X,
                    out var hitDistance)
                || hitDistance >= closestDistance)
            {
                continue;
            }

            closestTarget = target;
            closestDistance = hitDistance;
        }

        return closestTarget;
    }

    private static bool TryGetArrowHitDistance(
        float previousX,
        float nextX,
        float targetX,
        out float hitDistance)
    {
        var halfPlayerSize = GameProtocol.PlayerSize / 2f;
        var targetMinX = targetX - halfPlayerSize;
        var targetMaxX = targetX + halfPlayerSize;
        var segmentMinX = MathF.Min(previousX, nextX);
        var segmentMaxX = MathF.Max(previousX, nextX);
        if (segmentMaxX < targetMinX || segmentMinX > targetMaxX)
        {
            hitDistance = 0f;
            return false;
        }

        var hitX = nextX >= previousX
            ? MathF.Max(previousX, targetMinX)
            : MathF.Min(previousX, targetMaxX);
        hitDistance = MathF.Abs(hitX - previousX);
        return true;
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

    private void ApplyDamage(
        PlayerState player,
        int damage,
        float knockbackVelocityX)
    {
        player.Health = Math.Max(0, player.Health - damage);
        if (!player.IsDead)
        {
            player.KnockbackVelocityX = knockbackVelocityX;
            return;
        }

        player.DeathTimeSeconds = serverTimeSeconds;
        ClearControllableState(player, clearHeldActions: true);
        player.AttackTimeRemaining = 0f;
        player.AttackCooldownRemaining = 0f;
        player.VerticalVelocity = 0f;
        player.KnockbackVelocityX = 0f;
        player.Z = 0f;
    }

    private bool CanRevive(PlayerState player)
    {
        return player.DeathTimeSeconds.HasValue
            && serverTimeSeconds - player.DeathTimeSeconds.Value
                >= GameProtocol.ReviveDelaySeconds;
    }

    private byte GetReviveSecondsRemaining(PlayerState player)
    {
        if (!player.IsDead || !player.DeathTimeSeconds.HasValue)
        {
            return 0;
        }

        var remainingSeconds = GameProtocol.ReviveDelaySeconds
            - (serverTimeSeconds - player.DeathTimeSeconds.Value);
        return (byte)Math.Clamp(
            (int)Math.Ceiling(remainingSeconds),
            0,
            GameProtocol.ReviveDelaySeconds);
    }

    private static void Revive(PlayerState player)
    {
        player.Health = GameProtocol.MaxHealth;
        player.DeathTimeSeconds = null;
        ClearControllableState(player, clearHeldActions: false);
        player.AttackTimeRemaining = 0f;
        player.AttackCooldownRemaining = 0f;
        player.VerticalVelocity = 0f;
        player.KnockbackVelocityX = 0f;
        player.Z = 0f;
    }

    private static float MoveTowardsZero(float value, float maximumDelta)
    {
        if (MathF.Abs(value) <= maximumDelta)
        {
            return 0f;
        }

        return value - (MathF.Sign(value) * maximumDelta);
    }

    private static void ClearControllableState(
        PlayerState player,
        bool clearHeldActions)
    {
        player.Horizontal = 0;
        player.Depth = 0;
        player.PendingAttack = false;
        player.PendingJump = false;
        player.PendingRevive = false;
        if (clearHeldActions)
        {
            player.HeldActions = InputActionFlags.None;
        }
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

        public float KnockbackVelocityX { get; set; }

        public float AttackTimeRemaining { get; set; }

        public float AttackCooldownRemaining { get; set; }

        public int Health { get; set; } = GameProtocol.MaxHealth;

        public bool IsDead => Health == 0;

        public double? DeathTimeSeconds { get; set; }

        public FacingDirection Facing { get; set; } = FacingDirection.Right;

        public InputActionFlags HeldActions { get; set; }

        public bool PendingAttack { get; set; }

        public bool PendingJump { get; set; }

        public bool PendingRevive { get; set; }

        public uint LastInputSequence { get; set; }

        public bool HasInput { get; set; }
    }

    private readonly record struct PendingHit(int Damage, float KnockbackVelocityX);

    private sealed class ArrowState(
        int arrowId,
        int ownerPlayerId,
        float x,
        float y,
        float z,
        FacingDirection direction)
    {
        public int ArrowId { get; } = arrowId;

        public int OwnerPlayerId { get; } = ownerPlayerId;

        public float X { get; set; } = x;

        public float Y { get; } = y;

        public float Z { get; } = z;

        public FacingDirection Direction { get; } = direction;

        public float DistanceTraveled { get; set; }
    }
}
