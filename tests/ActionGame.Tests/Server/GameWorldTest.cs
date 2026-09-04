using ActionGame.Contracts.Protocol;
using ActionGame.Server.Game;

namespace ActionGame.Tests.Server;

public sealed class GameWorldTest
{
    private const float TickSeconds = 1f / GameProtocol.SimulationRate;

    [Fact]
    public void ThirdPlayerIsRejectedWhenWorldIsFull()
    {
        var world = new GameWorld();

        Assert.True(world.TryJoin(10, out var firstId, out _));
        Assert.True(world.TryJoin(20, out var secondId, out _));
        Assert.False(world.TryJoin(30, out _, out var rejectionReason));

        Assert.Equal(1, firstId);
        Assert.Equal(2, secondId);
        Assert.Equal(JoinRejectReason.ServerFull, rejectionReason);
    }

    [Fact]
    public void DuplicateJoinIsRejected()
    {
        var world = new GameWorld();
        Assert.True(world.TryJoin(10, out _, out _));

        Assert.False(world.TryJoin(10, out var playerId, out var rejectionReason));

        Assert.Equal(1, playerId);
        Assert.Equal(JoinRejectReason.AlreadyJoined, rejectionReason);
    }

    [Fact]
    public void ServerAppliesLatestInputAndRejectsStaleSequence()
    {
        var world = new GameWorld();
        Assert.True(world.TryJoin(10, out _, out _));
        Assert.True(world.TryGetPlayer(10, out var initial));

        Assert.True(world.ApplyInput(
            10,
            new InputCommandPacket(2, 1, 0, InputActionFlags.None)));
        Assert.False(world.ApplyInput(
            10,
            new InputCommandPacket(1, -1, 0, InputActionFlags.None)));
        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(10, out var moved));

        Assert.Equal(
            initial.X + (GameProtocol.HorizontalSpeed * TickSeconds),
            moved.X,
            precision: 4);
        Assert.Equal(initial.Y, moved.Y);
    }

    [Fact]
    public void DiagonalInputIsNormalizedBeforeAxisSpeedsAreApplied()
    {
        var horizontalWorld = new GameWorld();
        var diagonalWorld = new GameWorld();
        Assert.True(horizontalWorld.TryJoin(10, out _, out _));
        Assert.True(diagonalWorld.TryJoin(10, out _, out _));
        Assert.True(horizontalWorld.ApplyInput(
            10,
            new InputCommandPacket(1, 1, 0, InputActionFlags.None)));
        Assert.True(diagonalWorld.ApplyInput(
            10,
            new InputCommandPacket(1, 1, 1, InputActionFlags.None)));
        Assert.True(diagonalWorld.TryGetPlayer(10, out var initial));

        horizontalWorld.Update(TickSeconds);
        diagonalWorld.Update(TickSeconds);
        Assert.True(horizontalWorld.TryGetPlayer(10, out var horizontal));
        Assert.True(diagonalWorld.TryGetPlayer(10, out var diagonal));

        var inverseSqrtTwo = 1f / MathF.Sqrt(2f);
        Assert.Equal(
            GameProtocol.HorizontalSpeed * TickSeconds * inverseSqrtTwo,
            diagonal.X - initial.X,
            precision: 4);
        Assert.Equal(
            GameProtocol.DepthSpeed * TickSeconds * inverseSqrtTwo,
            diagonal.Y - initial.Y,
            precision: 4);
        Assert.True(horizontal.X > diagonal.X);
    }

    [Fact]
    public void PlayerPositionIsClampedInsideWorld()
    {
        var world = new GameWorld();
        Assert.True(world.TryJoin(10, out _, out _));
        Assert.True(world.ApplyInput(
            10,
            new InputCommandPacket(1, -1, 0, InputActionFlags.None)));

        world.Update(10f);
        Assert.True(world.TryGetPlayer(10, out var player));

        Assert.Equal(GameProtocol.PlayerSize / 2f, player.X);
    }

    [Fact]
    public void LeavingPlayerReleasesItsSlot()
    {
        var world = new GameWorld();
        Assert.True(world.TryJoin(10, out var firstId, out _));

        Assert.True(world.Leave(10));
        Assert.False(world.Leave(10));
        Assert.True(world.TryJoin(20, out var reusedId, out _));

        Assert.Equal(firstId, reusedId);
    }

    [Fact]
    public void OutOfRangeInputIsRejectedAtWorldBoundary()
    {
        var world = new GameWorld();
        Assert.True(world.TryJoin(10, out _, out _));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            world.ApplyInput(
                10,
                new InputCommandPacket(1, 2, 0, InputActionFlags.None)));
    }

    [Fact]
    public void JumpUsesServerGravityAndLandsOnGround()
    {
        var world = new GameWorld();
        Assert.True(world.TryJoin(10, out _, out _));
        Assert.True(world.ApplyInput(
            10,
            new InputCommandPacket(1, 0, 0, InputActionFlags.Jump)));

        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(10, out var airborne));
        Assert.True(airborne.Z > 0f);

        for (var index = 0; index < 30; index++)
        {
            world.Update(TickSeconds);
        }

        Assert.True(world.TryGetPlayer(10, out var landed));
        Assert.Equal(0f, landed.Z);
    }

    [Fact]
    public void JumpPressedAgainWhileAirborneDoesNotDoubleJump()
    {
        var secondPressWorld = new GameWorld();
        var controlWorld = new GameWorld();
        Assert.True(secondPressWorld.TryJoin(10, out _, out _));
        Assert.True(controlWorld.TryJoin(10, out _, out _));

        ApplyToBoth(new InputCommandPacket(1, 0, 0, InputActionFlags.Jump));
        UpdateBoth();
        ApplyToBoth(new InputCommandPacket(2, 0, 0, InputActionFlags.None));
        UpdateBoth();
        Assert.True(secondPressWorld.ApplyInput(
            10,
            new InputCommandPacket(3, 0, 0, InputActionFlags.Jump)));
        Assert.True(controlWorld.ApplyInput(
            10,
            new InputCommandPacket(3, 0, 0, InputActionFlags.None)));
        UpdateBoth();

        Assert.True(secondPressWorld.TryGetPlayer(10, out var secondPressPlayer));
        Assert.True(controlWorld.TryGetPlayer(10, out var controlPlayer));
        Assert.Equal(controlPlayer.Z, secondPressPlayer.Z, precision: 4);

        void ApplyToBoth(InputCommandPacket input)
        {
            Assert.True(secondPressWorld.ApplyInput(10, input));
            Assert.True(controlWorld.ApplyInput(10, input));
        }

        void UpdateBoth()
        {
            secondPressWorld.Update(TickSeconds);
            controlWorld.Update(TickSeconds);
        }
    }

    [Fact]
    public void AttackUsesFacingAndExpires()
    {
        var world = new GameWorld();
        Assert.True(world.TryJoin(10, out _, out _));
        Assert.True(world.ApplyInput(
            10,
            new InputCommandPacket(1, -1, 0, InputActionFlags.Attack)));

        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(10, out var attacking));
        Assert.Equal(FacingDirection.Left, attacking.Facing);
        Assert.True(attacking.IsAttacking);

        for (var index = 0; index < 4; index++)
        {
            world.Update(TickSeconds);
        }

        Assert.True(world.TryGetPlayer(10, out var finished));
        Assert.False(finished.IsAttacking);
    }

    [Fact]
    public void AttackInRangeDealsDamageOnlyOncePerAction()
    {
        var world = CreateWorldWithPlayersInAttackRange();
        Assert.True(world.ApplyInput(
            10,
            new InputCommandPacket(3, 0, 0, InputActionFlags.Attack)));

        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(20, out var damaged));
        Assert.Equal(GameProtocol.MaxHealth - GameProtocol.AttackDamage, damaged.Health);

        for (var index = 0; index < 3; index++)
        {
            world.Update(TickSeconds);
        }

        Assert.True(world.TryGetPlayer(20, out var afterAttack));
        Assert.Equal(damaged.Health, afterAttack.Health);
    }

    [Fact]
    public void MeleeKnockbackPushesTargetAndDecelerates()
    {
        var world = CreateWorldWithPlayersInAttackRange();
        Assert.True(world.TryGetPlayer(20, out var beforeHit));
        Assert.True(world.ApplyInput(
            10,
            new InputCommandPacket(3, 0, 0, InputActionFlags.Attack)));
        world.Update(TickSeconds);

        Assert.True(world.ApplyInput(
            20,
            new InputCommandPacket(3, -1, 0, InputActionFlags.None)));
        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(20, out var firstKnockbackTick));
        Assert.True(firstKnockbackTick.X > beforeHit.X);

        Assert.True(world.ApplyInput(
            20,
            new InputCommandPacket(4, 0, 0, InputActionFlags.None)));
        for (var index = 0; index < 5; index++)
        {
            world.Update(TickSeconds);
        }

        Assert.True(world.TryGetPlayer(20, out var settled));
        Assert.InRange(settled.X - beforeHit.X, 35f, 40f);
        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(20, out var afterSettling));
        Assert.Equal(settled.X, afterSettling.X);
    }

    [Fact]
    public void ArrowKnockbackIsWeakerAndFollowsArrowDirection()
    {
        var world = CreateWorldWithPlayersInAttackRange();
        Assert.True(world.TryGetPlayer(10, out var beforeHit));
        Assert.True(world.ApplyInput(
            20,
            new InputCommandPacket(3, 0, 0, InputActionFlags.Attack)));
        world.Update(TickSeconds);
        world.Update(TickSeconds);

        Assert.True(world.TryGetPlayer(10, out var hit));
        Assert.Equal(
            GameProtocol.MaxHealth - GameProtocol.RangerAttackDamage,
            hit.Health);
        for (var index = 0; index < 4; index++)
        {
            world.Update(TickSeconds);
        }

        Assert.True(world.TryGetPlayer(10, out var settled));
        Assert.InRange(beforeHit.X - settled.X, 14f, 16f);
        Assert.True(
            GameProtocol.ArrowKnockbackSpeed < GameProtocol.MeleeKnockbackSpeed);
        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(10, out var afterSettling));
        Assert.Equal(settled.X, afterSettling.X);
    }

    [Fact]
    public void KnockbackStopsAtWorldBoundary()
    {
        var world = new GameWorld();
        Assert.True(world.TryJoin(10, out _, out _));
        Assert.True(world.TryJoin(20, out _, out _));
        Assert.True(world.ApplyInput(
            10,
            new InputCommandPacket(1, 1, 0, InputActionFlags.None)));
        Assert.True(world.ApplyInput(
            20,
            new InputCommandPacket(1, 1, 0, InputActionFlags.None)));
        for (var index = 0; index < 100; index++)
        {
            world.Update(TickSeconds);
        }

        Assert.True(world.ApplyInput(
            10,
            new InputCommandPacket(2, 0, 0, InputActionFlags.None)));
        Assert.True(world.ApplyInput(
            20,
            new InputCommandPacket(2, 0, 0, InputActionFlags.None)));
        world.Update(TickSeconds);
        Assert.True(world.ApplyInput(
            10,
            new InputCommandPacket(3, 0, 0, InputActionFlags.Attack)));
        world.Update(TickSeconds);
        world.Update(TickSeconds);

        Assert.True(world.TryGetPlayer(20, out var atBoundary));
        var maximumX = GameProtocol.WorldWidth - (GameProtocol.PlayerSize / 2f);
        Assert.Equal(maximumX, atBoundary.X);
        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(20, out var stillAtBoundary));
        Assert.Equal(atBoundary.X, stillAtBoundary.X);
    }

    [Fact]
    public void AttackOutsideDepthRangeDoesNotDealDamage()
    {
        var world = CreateWorldWithPlayersInAttackRange();
        Assert.True(world.ApplyInput(
            20,
            new InputCommandPacket(3, 0, 1, InputActionFlags.None)));
        for (var index = 0; index < 5; index++)
        {
            world.Update(TickSeconds);
        }

        Assert.True(world.ApplyInput(
            10,
            new InputCommandPacket(3, 0, 0, InputActionFlags.Attack)));
        world.Update(TickSeconds);

        Assert.True(world.TryGetPlayer(20, out var target));
        Assert.Equal(GameProtocol.MaxHealth, target.Health);
    }

    [Fact]
    public void MeleeAndRangedAttacksResolveWithDifferentDamageTiming()
    {
        var world = CreateWorldWithPlayersInAttackRange();
        Assert.True(world.ApplyInput(
            10,
            new InputCommandPacket(3, 0, 0, InputActionFlags.Attack)));
        Assert.True(world.ApplyInput(
            20,
            new InputCommandPacket(3, 0, 0, InputActionFlags.Attack)));

        world.Update(TickSeconds);

        Assert.True(world.TryGetPlayer(10, out var first));
        Assert.True(world.TryGetPlayer(20, out var second));
        Assert.Equal(GameProtocol.MaxHealth, first.Health);
        Assert.Equal(GameProtocol.MaxHealth - GameProtocol.AttackDamage, second.Health);
        var flyingArrow = Assert.Single(world.CreateSnapshot().Arrows);
        Assert.Equal(GameProtocol.RangerPlayerId, flyingArrow.OwnerPlayerId);
        Assert.Equal(FacingDirection.Left, flyingArrow.Direction);

        world.Update(TickSeconds);

        Assert.True(world.TryGetPlayer(10, out var arrowTarget));
        Assert.Equal(
            GameProtocol.MaxHealth - GameProtocol.RangerAttackDamage,
            arrowTarget.Health);
        Assert.Empty(world.CreateSnapshot().Arrows);
        Assert.True(GameProtocol.RangerAttackDamage < GameProtocol.AttackDamage);
    }

    [Fact]
    public void ArrowThatMissesDisappearsAfterMaximumDistance()
    {
        var world = CreateWorldWithPlayersInAttackRange();
        Assert.True(world.ApplyInput(
            10,
            new InputCommandPacket(3, 0, 1, InputActionFlags.None)));
        for (var index = 0; index < 5; index++)
        {
            world.Update(TickSeconds);
        }

        Assert.True(world.ApplyInput(
            10,
            new InputCommandPacket(4, 0, 0, InputActionFlags.None)));
        Assert.True(world.ApplyInput(
            20,
            new InputCommandPacket(3, 0, 0, InputActionFlags.Attack)));
        world.Update(TickSeconds);
        Assert.Single(world.CreateSnapshot().Arrows);

        var maximumFlightTicks = (int)MathF.Ceiling(
            GameProtocol.ArrowMaxDistance
                / (GameProtocol.ArrowSpeed * TickSeconds));
        for (var index = 1; index < maximumFlightTicks; index++)
        {
            world.Update(TickSeconds);
        }

        Assert.True(world.TryGetPlayer(10, out var missedTarget));
        Assert.Equal(GameProtocol.MaxHealth, missedTarget.Health);
        Assert.Empty(world.CreateSnapshot().Arrows);
    }

    [Fact]
    public void ZeroHealthMarksPlayerDeadAndIgnoresFurtherInput()
    {
        var world = CreateWorldWithPlayersInAttackRange();
        KillSecondPlayer(world);

        Assert.True(world.TryGetPlayer(20, out var dead));
        Assert.Equal(0, dead.Health);
        Assert.True(dead.IsDead);
        Assert.False(dead.IsAttacking);

        Assert.True(world.ApplyInput(
            20,
            new InputCommandPacket(
                3,
                1,
                1,
                InputActionFlags.Attack | InputActionFlags.Jump)));
        world.Update(TickSeconds);

        Assert.True(world.TryGetPlayer(20, out var afterInput));
        Assert.Equal(dead.X, afterInput.X);
        Assert.Equal(dead.Y, afterInput.Y);
        Assert.Equal(0f, afterInput.Z);
        Assert.Equal(0, afterInput.Health);
        Assert.True(afterInput.IsDead);
        Assert.False(afterInput.IsAttacking);
    }

    [Fact]
    public void ReviveRequestBeforeDelayIsRejected()
    {
        var world = CreateWorldWithPlayersInAttackRange();
        KillSecondPlayer(world);

        Assert.True(world.ApplyInput(
            20,
            new InputCommandPacket(3, 0, 0, InputActionFlags.Revive)));
        world.Update(TickSeconds);

        Assert.True(world.TryGetPlayer(20, out var player));
        Assert.Equal(0, player.Health);
        Assert.True(player.IsDead);
    }

    [Fact]
    public void ReviveCountdownUsesRecordedServerDeathTime()
    {
        var world = CreateWorldWithPlayersInAttackRange();
        KillSecondPlayer(world);

        var dead = Assert.Single(
            world.CreateSnapshot().Players,
            player => player.PlayerId == GameProtocol.RangerPlayerId);
        Assert.Equal(5, dead.ReviveSecondsRemaining);

        for (var index = 0; index < GameProtocol.SimulationRate; index++)
        {
            world.Update(TickSeconds);
        }

        var afterOneSecond = Assert.Single(
            world.CreateSnapshot().Players,
            player => player.PlayerId == GameProtocol.RangerPlayerId);
        Assert.Equal(4, afterOneSecond.ReviveSecondsRemaining);

        for (var index = GameProtocol.SimulationRate;
            index < GameProtocol.ReviveDelaySeconds * GameProtocol.SimulationRate;
            index++)
        {
            world.Update(TickSeconds);
        }

        var ready = Assert.Single(
            world.CreateSnapshot().Players,
            player => player.PlayerId == GameProtocol.RangerPlayerId);
        Assert.True(ready.IsDead);
        Assert.Equal(0, ready.ReviveSecondsRemaining);
    }

    [Fact]
    public void ReviveRequiresNewRequestAfterDelayAndRestoresPlayer()
    {
        var world = CreateWorldWithPlayersInAttackRange();
        KillSecondPlayer(world);
        Assert.True(world.TryGetPlayer(20, out var dead));

        uint sequence = 3;
        Assert.True(world.ApplyInput(
            20,
            new InputCommandPacket(
                sequence++,
                0,
                0,
                InputActionFlags.Revive)));
        world.Update(TickSeconds);

        var requiredTicks = (int)(
            GameProtocol.ReviveDelaySeconds * GameProtocol.SimulationRate);
        for (var index = 0; index < requiredTicks; index++)
        {
            Assert.True(world.ApplyInput(
                20,
                new InputCommandPacket(
                    sequence++,
                    0,
                    0,
                    InputActionFlags.Revive)));
            world.Update(TickSeconds);
        }

        Assert.True(world.TryGetPlayer(20, out var stillDead));
        Assert.True(stillDead.IsDead);

        Assert.True(world.ApplyInput(
            20,
            new InputCommandPacket(
                sequence++,
                0,
                0,
                InputActionFlags.None)));
        world.Update(TickSeconds);
        Assert.True(world.ApplyInput(
            20,
            new InputCommandPacket(
                sequence++,
                0,
                0,
                InputActionFlags.Revive)));
        world.Update(TickSeconds);

        Assert.True(world.TryGetPlayer(20, out var revived));
        Assert.False(revived.IsDead);
        Assert.Equal(GameProtocol.MaxHealth, revived.Health);
        Assert.Equal(dead.X, revived.X);
        Assert.Equal(dead.Y, revived.Y);

        Assert.True(world.ApplyInput(
            20,
            new InputCommandPacket(
                sequence++,
                0,
                0,
                InputActionFlags.Attack)));
        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(20, out var heldAfterRevive));
        Assert.False(heldAfterRevive.IsAttacking);

        Assert.True(world.ApplyInput(
            20,
            new InputCommandPacket(
                sequence++,
                0,
                0,
                InputActionFlags.None)));
        world.Update(TickSeconds);
        Assert.True(world.ApplyInput(
            20,
            new InputCommandPacket(
                sequence++,
                -1,
                0,
                InputActionFlags.Jump)));
        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(20, out var active));
        Assert.True(active.X < revived.X);
        Assert.True(active.Z > 0f);
    }

    [Fact]
    public void ReservedZInputDoesNotChangeActionState()
    {
        var world = new GameWorld();
        Assert.True(world.TryJoin(10, out _, out _));
        Assert.True(world.ApplyInput(
            10,
            new InputCommandPacket(1, 0, 0, InputActionFlags.ReservedZ)));

        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(10, out var player));

        Assert.Equal(0f, player.Z);
        Assert.False(player.IsAttacking);
    }

    private static GameWorld CreateWorldWithPlayersInAttackRange()
    {
        var world = new GameWorld();
        Assert.True(world.TryJoin(10, out _, out _));
        Assert.True(world.TryJoin(20, out _, out _));
        Assert.True(world.ApplyInput(
            10,
            new InputCommandPacket(1, 1, 0, InputActionFlags.None)));
        Assert.True(world.ApplyInput(
            20,
            new InputCommandPacket(1, -1, 0, InputActionFlags.None)));
        for (var index = 0; index < 30; index++)
        {
            world.Update(TickSeconds);
        }

        Assert.True(world.ApplyInput(
            10,
            new InputCommandPacket(2, 0, 0, InputActionFlags.None)));
        Assert.True(world.ApplyInput(
            20,
            new InputCommandPacket(2, 0, 0, InputActionFlags.None)));
        world.Update(TickSeconds);
        return world;
    }

    private static void KillSecondPlayer(GameWorld world)
    {
        uint sequence = 3;
        var attackCount = GameProtocol.MaxHealth / GameProtocol.AttackDamage;
        for (var attackIndex = 0; attackIndex < attackCount; attackIndex++)
        {
            Assert.True(world.ApplyInput(
                10,
                new InputCommandPacket(
                    sequence++,
                    0,
                    0,
                    InputActionFlags.Attack)));
            world.Update(TickSeconds);
            if (attackIndex == attackCount - 1)
            {
                continue;
            }

            Assert.True(world.ApplyInput(
                10,
                new InputCommandPacket(
                    sequence++,
                    1,
                    0,
                    InputActionFlags.None)));
            for (var followTick = 0; followTick < 4; followTick++)
            {
                world.Update(TickSeconds);
            }

            Assert.True(world.ApplyInput(
                10,
                new InputCommandPacket(
                    sequence++,
                    0,
                    0,
                    InputActionFlags.None)));
            for (var cooldownTick = 4; cooldownTick < 7; cooldownTick++)
            {
                world.Update(TickSeconds);
            }
        }
    }
}
