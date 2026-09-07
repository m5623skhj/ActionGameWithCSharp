using ActionGame.Contracts.Protocol;

namespace ActionGame.Server.Game;

public sealed class GameWorld
{
    private const int MaximumPendingActions = 8;
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

    public bool ApplyMovementInput(long connectionId, MovementInputPacket input)
    {
        if (input.Horizontal is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        if (input.Depth is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        if (!playersByConnection.TryGetValue(connectionId, out var player))
        {
            return false;
        }

        if (player.HasMovementInput
            && input.Sequence <= player.LastMovementInputSequence)
        {
            return false;
        }

        player.HasMovementInput = true;
        player.LastMovementInputSequence = input.Sequence;
        if (player.IsDead || player.HitStunTimeRemaining > 0f)
        {
            player.Horizontal = 0;
            player.Depth = 0;
            return true;
        }

        player.Horizontal = input.Horizontal;
        player.Depth = input.Depth;
        return true;
    }

    public bool ApplyActionCommand(long connectionId, ActionCommandPacket command)
    {
        if (!Enum.IsDefined(command.ActionId))
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }

        if (!playersByConnection.TryGetValue(connectionId, out var player))
        {
            return false;
        }

        if (player.HasActionCommand
            && command.Sequence <= player.LastActionCommandSequence)
        {
            return false;
        }

        player.HasActionCommand = true;
        player.LastActionCommandSequence = command.Sequence;
        if (!IsActionAllowedForPlayer(player, command.ActionId))
        {
            return false;
        }

        if (player.IsDead)
        {
            if (command.ActionId == ActionId.Revive)
            {
                EnqueueAction(player, command.ActionId);
            }

            return true;
        }

        if (player.HitStunTimeRemaining > 0f
            || command.ActionId == ActionId.Revive)
        {
            return true;
        }

        EnqueueAction(player, command.ActionId);
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
        var pendingSkills = new List<PendingSkill>();
        foreach (var player in playersByConnection.Values)
        {
            player.AttackTimeRemaining = Math.Max(
                0f,
                player.AttackTimeRemaining - deltaSeconds);
            player.AttackCooldownRemaining = Math.Max(
                0f,
                player.AttackCooldownRemaining - deltaSeconds);
            player.HitStunTimeRemaining = Math.Max(
                0f,
                player.HitStunTimeRemaining - deltaSeconds);
            player.InvulnerabilityTimeRemaining = Math.Max(
                0f,
                player.InvulnerabilityTimeRemaining - deltaSeconds);
            player.SkillCooldownRemaining = Math.Max(
                0f,
                player.SkillCooldownRemaining - deltaSeconds);
            if (player.IsDead)
            {
                var reviveRequested = DrainReviveRequest(player);
                if (reviveRequested && CanRevive(player))
                {
                    Revive(player);
                    continue;
                }

                ClearControllableState(player);
                player.AttackTimeRemaining = 0f;
                player.AttackCooldownRemaining = 0f;
                player.SkillCooldownRemaining = 0f;
                player.VerticalVelocity = 0f;
                player.KnockbackVelocityX = 0f;
                player.HitStunTimeRemaining = 0f;
                player.InvulnerabilityTimeRemaining = 0f;
                player.Z = 0f;
                continue;
            }

            if (player.HitStunTimeRemaining > 0f)
            {
                ClearControllableState(player);
            }
            else
            {
                var requestedAction = DrainRequestedActions(player, out var jumpRequested);
                if (jumpRequested && player.Z <= 0f)
                {
                    player.VerticalVelocity = GameProtocol.JumpInitialVelocity;
                }

                if ((requestedAction is ActionId.WarriorDashSlash
                        or ActionId.RangerPowerArrow)
                    && player.SkillCooldownRemaining <= 0f)
                {
                    player.AttackTimeRemaining = GameProtocol.MeleeSkillDuration;
                    player.SkillCooldownRemaining = GameProtocol.SkillCooldown;
                    player.AttackCooldownRemaining = GameProtocol.AttackCooldown;
                    pendingSkills.Add(new PendingSkill(player, requestedAction.Value));
                }
                else if (requestedAction == ActionId.BasicAttack
                    && player.AttackCooldownRemaining <= 0f)
                {
                    player.AttackTimeRemaining = GameProtocol.AttackDuration;
                    player.AttackCooldownRemaining = GameProtocol.AttackCooldown;
                    attackers.Add(player);
                }

            }

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

        ResolveSkills(pendingSkills);
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
                GetReviveSecondsRemaining(player),
                player.InvulnerabilityTimeRemaining > 0f,
                player.SkillCooldownRemaining))
            .ToArray();
        var arrowSnapshots = arrows
            .OrderBy(arrow => arrow.ArrowId)
            .Select(arrow => new ArrowSnapshot(
                arrow.ArrowId,
                arrow.OwnerPlayerId,
                arrow.X,
                arrow.Y,
                arrow.Z,
                arrow.Direction,
                arrow.IsSkillArrow))
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
                GetReviveSecondsRemaining(state),
                state.InvulnerabilityTimeRemaining > 0f,
                state.SkillCooldownRemaining);
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

    private static bool IsActionAllowedForPlayer(PlayerState player, ActionId actionId)
    {
        return actionId switch
        {
            ActionId.BasicAttack or ActionId.Jump or ActionId.Revive => true,
            ActionId.WarriorDashSlash => player.PlayerId != GameProtocol.RangerPlayerId,
            ActionId.RangerPowerArrow => player.PlayerId == GameProtocol.RangerPlayerId,
            _ => false,
        };
    }

    private static void EnqueueAction(PlayerState player, ActionId actionId)
    {
        if (player.PendingActions.Count < MaximumPendingActions)
        {
            player.PendingActions.Enqueue(actionId);
        }
    }

    private static bool DrainReviveRequest(PlayerState player)
    {
        var reviveRequested = false;
        while (player.PendingActions.TryDequeue(out var actionId))
        {
            reviveRequested |= actionId == ActionId.Revive;
        }

        return reviveRequested;
    }

    private static ActionId? DrainRequestedActions(
        PlayerState player,
        out bool jumpRequested)
    {
        ActionId? offensiveAction = null;
        jumpRequested = false;
        while (player.PendingActions.TryDequeue(out var actionId))
        {
            if (actionId == ActionId.Jump)
            {
                jumpRequested = true;
            }
            else if (offensiveAction is null && actionId != ActionId.Revive)
            {
                offensiveAction = actionId;
            }
        }

        return offensiveAction;
    }

    private void ResolveAttacks(IReadOnlyList<PlayerState> attackers)
    {
        var pendingHits = new Dictionary<PlayerState, PendingHit>();
        foreach (var attacker in attackers)
        {
            if (attacker.PlayerId == GameProtocol.RangerPlayerId)
            {
                SpawnArrow(attacker, isSkillArrow: false);
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
                    GameProtocol.MeleeKnockbackSpeed),
                GameProtocol.MeleeHitStunDuration);
        }
    }

    /// <summary>
    /// Resolves every skill accepted for this tick from an immutable position snapshot.
    /// Skill effects are committed before damage so container iteration order cannot
    /// cancel another action that was valid at the start of the resolution phase.
    /// </summary>
    private void ResolveSkills(IReadOnlyList<PendingSkill> pendingSkills)
    {
        if (pendingSkills.Count == 0)
        {
            return;
        }

        var playerSnapshots = playersByConnection.Values.ToDictionary(
            player => player,
            player => new SkillPlayerSnapshot(
                player.X,
                player.Y,
                player.Z,
                player.Facing,
                player.IsDead));
        var skillIntents = pendingSkills
            .Select(pendingSkill =>
            {
                var snapshot = playerSnapshots[pendingSkill.Player];
                return new SkillIntent(
                    pendingSkill.Player,
                    pendingSkill.ActionId,
                    snapshot.X,
                    snapshot.Y,
                    snapshot.Z,
                    snapshot.Facing);
            })
            .OrderBy(intent => intent.Player.PlayerId)
            .ToArray();
        var pendingHits = new Dictionary<PlayerState, PendingSkillHit>();

        foreach (var intent in skillIntents)
        {
            switch (intent.ActionId)
            {
                case ActionId.WarriorDashSlash:
                    ResolveMeleeSkill(intent, playerSnapshots, pendingHits);
                    break;
                case ActionId.RangerPowerArrow:
                    SpawnArrow(intent);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported skill action: {intent.ActionId}.");
            }
        }

        foreach (var pair in pendingHits)
        {
            ApplyDamage(
                pair.Key,
                pair.Value.Damage,
                Math.Clamp(
                    pair.Value.KnockbackVelocityX,
                    -GameProtocol.MeleeSkillKnockbackSpeed,
                    GameProtocol.MeleeSkillKnockbackSpeed),
                pair.Value.HitStunDuration);
        }
    }

    private static void ResolveMeleeSkill(
        SkillIntent intent,
        IReadOnlyDictionary<PlayerState, SkillPlayerSnapshot> playerSnapshots,
        IDictionary<PlayerState, PendingSkillHit> pendingHits)
    {
        var directionMultiplier = intent.Facing == FacingDirection.Right ? 1f : -1f;
        var startX = intent.X;
        var halfPlayerSize = GameProtocol.PlayerSize / 2f;
        var endX = Math.Clamp(
            startX + (directionMultiplier * GameProtocol.MeleeSkillDashDistance),
            halfPlayerSize,
            GameProtocol.WorldWidth - halfPlayerSize);
        intent.Player.X = endX;
        intent.Player.KnockbackVelocityX = 0f;

        foreach (var pair in playerSnapshots)
        {
            var target = pair.Key;
            var targetSnapshot = pair.Value;
            if (ReferenceEquals(intent.Player, target)
                || targetSnapshot.IsDead
                || MathF.Abs(targetSnapshot.Y - intent.Y)
                    > GameProtocol.AttackDepthTolerance
                || MathF.Abs(targetSnapshot.Z - intent.Z)
                    > GameProtocol.AttackHeightTolerance
                || !TryGetArrowHitDistance(
                    startX,
                    endX,
                    targetSnapshot.X,
                    out _))
            {
                continue;
            }

            pendingHits.TryGetValue(target, out var hit);
            pendingHits[target] = new PendingSkillHit(
                hit.Damage + GameProtocol.MeleeSkillDamage,
                hit.KnockbackVelocityX
                    + (directionMultiplier * GameProtocol.MeleeSkillKnockbackSpeed),
                Math.Max(
                    hit.HitStunDuration,
                    GameProtocol.MeleeSkillHitStunDuration));
        }
    }

    private void SpawnArrow(SkillIntent intent)
    {
        SpawnArrow(
            intent.Player.PlayerId,
            intent.X,
            intent.Y,
            intent.Z,
            intent.Facing,
            isSkillArrow: true);
    }

    private void SpawnArrow(PlayerState attacker, bool isSkillArrow)
    {
        SpawnArrow(
            attacker.PlayerId,
            attacker.X,
            attacker.Y,
            attacker.Z,
            attacker.Facing,
            isSkillArrow);
    }

    private void SpawnArrow(
        int ownerPlayerId,
        float x,
        float y,
        float z,
        FacingDirection facing,
        bool isSkillArrow)
    {
        if (arrows.Count >= GameProtocol.MaxArrows)
        {
            return;
        }

        var directionMultiplier = facing == FacingDirection.Right ? 1f : -1f;
        arrows.Add(new ArrowState(
            nextArrowId++,
            ownerPlayerId,
            x + (directionMultiplier * GameProtocol.PlayerSize / 2f),
            y,
            z + GameProtocol.ArrowSpawnHeight,
            facing,
            isSkillArrow));
    }

    private void UpdateArrows(float deltaSeconds)
    {
        for (var index = arrows.Count - 1; index >= 0; index--)
        {
            var arrow = arrows[index];
            var remainingDistance = arrow.MaximumDistance - arrow.DistanceTraveled;
            if (remainingDistance <= 0f)
            {
                arrows.RemoveAt(index);
                continue;
            }

            var travelDistance = MathF.Min(
                arrow.Speed * deltaSeconds,
                remainingDistance);
            var directionMultiplier = arrow.Direction == FacingDirection.Right ? 1f : -1f;
            var previousX = arrow.X;
            var nextX = previousX + (directionMultiplier * travelDistance);
            var target = FindArrowTarget(arrow, previousX, nextX);
            if (target is not null)
            {
                var knockbackVelocityX = arrow.Direction == FacingDirection.Right
                    ? arrow.KnockbackSpeed
                    : -arrow.KnockbackSpeed;
                ApplyDamage(
                    target,
                    arrow.Damage,
                    knockbackVelocityX,
                    arrow.HitStunDuration);
                arrows.RemoveAt(index);
                continue;
            }

            arrow.X = nextX;
            arrow.DistanceTraveled += travelDistance;
            if (arrow.DistanceTraveled >= arrow.MaximumDistance
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
        float knockbackVelocityX,
        float hitStunDuration)
    {
        if (player.InvulnerabilityTimeRemaining > 0f)
        {
            return;
        }

        player.Health = Math.Max(0, player.Health - damage);
        if (!player.IsDead)
        {
            ClearControllableState(player);
            player.AttackTimeRemaining = 0f;
            player.KnockbackVelocityX = knockbackVelocityX;
            player.HitStunTimeRemaining = hitStunDuration;
            player.InvulnerabilityTimeRemaining =
                GameProtocol.HitInvulnerabilityDuration;
            return;
        }

        player.DeathTimeSeconds = serverTimeSeconds;
        ClearControllableState(player);
        player.AttackTimeRemaining = 0f;
        player.AttackCooldownRemaining = 0f;
        player.SkillCooldownRemaining = 0f;
        player.VerticalVelocity = 0f;
        player.KnockbackVelocityX = 0f;
        player.HitStunTimeRemaining = 0f;
        player.InvulnerabilityTimeRemaining = 0f;
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
        ClearControllableState(player);
        player.AttackTimeRemaining = 0f;
        player.AttackCooldownRemaining = 0f;
        player.SkillCooldownRemaining = 0f;
        player.VerticalVelocity = 0f;
        player.KnockbackVelocityX = 0f;
        player.HitStunTimeRemaining = 0f;
        player.InvulnerabilityTimeRemaining = 0f;
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

    private static void ClearControllableState(PlayerState player)
    {
        player.Horizontal = 0;
        player.Depth = 0;
        player.PendingActions.Clear();
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

        public float HitStunTimeRemaining { get; set; }

        public float InvulnerabilityTimeRemaining { get; set; }

        public float AttackTimeRemaining { get; set; }

        public float AttackCooldownRemaining { get; set; }

        public float SkillCooldownRemaining { get; set; }

        public int Health { get; set; } = GameProtocol.MaxHealth;

        public bool IsDead => Health == 0;

        public double? DeathTimeSeconds { get; set; }

        public FacingDirection Facing { get; set; } = FacingDirection.Right;

        public Queue<ActionId> PendingActions { get; } = [];

        public uint LastMovementInputSequence { get; set; }

        public bool HasMovementInput { get; set; }

        public uint LastActionCommandSequence { get; set; }

        public bool HasActionCommand { get; set; }
    }

    private readonly record struct PendingHit(int Damage, float KnockbackVelocityX);

    private readonly record struct PendingSkill(PlayerState Player, ActionId ActionId);

    private readonly record struct SkillPlayerSnapshot(
        float X,
        float Y,
        float Z,
        FacingDirection Facing,
        bool IsDead);

    private readonly record struct SkillIntent(
        PlayerState Player,
        ActionId ActionId,
        float X,
        float Y,
        float Z,
        FacingDirection Facing);

    private readonly record struct PendingSkillHit(
        int Damage,
        float KnockbackVelocityX,
        float HitStunDuration);

    private sealed class ArrowState(
        int arrowId,
        int ownerPlayerId,
        float x,
        float y,
        float z,
        FacingDirection direction,
        bool isSkillArrow)
    {
        public int ArrowId { get; } = arrowId;

        public int OwnerPlayerId { get; } = ownerPlayerId;

        public float X { get; set; } = x;

        public float Y { get; } = y;

        public float Z { get; } = z;

        public FacingDirection Direction { get; } = direction;

        public bool IsSkillArrow { get; } = isSkillArrow;

        public int Damage { get; } = isSkillArrow
            ? GameProtocol.RangerSkillDamage
            : GameProtocol.RangerAttackDamage;

        public float Speed { get; } = isSkillArrow
            ? GameProtocol.RangerSkillArrowSpeed
            : GameProtocol.ArrowSpeed;

        public float MaximumDistance { get; } = isSkillArrow
            ? GameProtocol.RangerSkillArrowMaxDistance
            : GameProtocol.ArrowMaxDistance;

        public float KnockbackSpeed { get; } = isSkillArrow
            ? GameProtocol.RangerSkillKnockbackSpeed
            : GameProtocol.ArrowKnockbackSpeed;

        public float HitStunDuration { get; } = isSkillArrow
            ? GameProtocol.RangerSkillHitStunDuration
            : GameProtocol.ArrowHitStunDuration;

        public float DistanceTraveled { get; set; }
    }
}
