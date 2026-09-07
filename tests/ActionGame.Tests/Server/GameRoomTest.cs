using ActionGame.Contracts.Protocol;
using ActionGame.Server.Game;

namespace ActionGame.Tests.Server;

public sealed class GameRoomTest
{
    private const float TickSeconds = 1f / GameProtocol.SimulationRate;

    [Fact]
    public void ThirdPlayerIsRejectedWhenWorldIsFull()
    {
        var world = new GameRoom();

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
        var world = new GameRoom();
        Assert.True(world.TryJoin(10, out _, out _));

        Assert.False(world.TryJoin(10, out var playerId, out var rejectionReason));

        Assert.Equal(1, playerId);
        Assert.Equal(JoinRejectReason.AlreadyJoined, rejectionReason);
    }

    [Fact]
    public void ServerAppliesLatestInputAndRejectsStaleSequence()
    {
        var world = new GameRoom();
        Assert.True(world.TryJoin(10, out _, out _));
        Assert.True(world.TryGetPlayer(10, out var initial));

        Assert.True(world.ApplyMovementInput(
            10,
            new MovementInputPacket(2, 1, 0)));
        Assert.False(world.ApplyMovementInput(
            10,
            new MovementInputPacket(1, -1, 0)));
        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(10, out var moved));

        Assert.Equal(
            initial.X + (GameProtocol.HorizontalSpeed * TickSeconds),
            moved.X,
            precision: 4);
        Assert.Equal(initial.Y, moved.Y);
    }

    [Fact]
    public void ActionCommandsUseAnIndependentSequence()
    {
        var world = new GameRoom();
        Assert.True(world.TryJoin(10, out _, out _));

        Assert.True(world.ApplyActionCommand(
            10,
            new ActionCommandPacket(2, ActionId.BasicAttack)));
        Assert.False(world.ApplyActionCommand(
            10,
            new ActionCommandPacket(1, ActionId.Jump)));
        Assert.True(world.ApplyMovementInput(
            10,
            new MovementInputPacket(1, 1, 0)));

        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(10, out var player));
        Assert.True(player.IsAttacking);
        Assert.Equal(0f, player.Z);
        Assert.True(player.X > 100f);
    }

    [Fact]
    public void CharacterSpecificActionForAnotherCharacterIsRejected()
    {
        var world = new GameRoom();
        Assert.True(world.TryJoin(10, out _, out _));
        Assert.True(world.TryJoin(20, out _, out _));

        Assert.False(world.ApplyActionCommand(
            10,
            new ActionCommandPacket(1, ActionId.RangerPowerArrow)));
        Assert.False(world.ApplyActionCommand(
            20,
            new ActionCommandPacket(1, ActionId.WarriorDashSlash)));

        world.Update(TickSeconds);
        Assert.Empty(world.CreateSnapshot().Arrows);
        Assert.True(world.TryGetPlayer(10, out var warrior));
        Assert.True(world.TryGetPlayer(20, out var ranger));
        Assert.False(warrior.IsAttacking);
        Assert.False(ranger.IsAttacking);
    }

    [Fact]
    public void DiagonalInputIsNormalizedBeforeAxisSpeedsAreApplied()
    {
        var horizontalWorld = new GameRoom();
        var diagonalWorld = new GameRoom();
        Assert.True(horizontalWorld.TryJoin(10, out _, out _));
        Assert.True(diagonalWorld.TryJoin(10, out _, out _));
        Assert.True(horizontalWorld.ApplyMovementInput(
            10,
            new MovementInputPacket(1, 1, 0)));
        Assert.True(diagonalWorld.ApplyMovementInput(
            10,
            new MovementInputPacket(1, 1, 1)));
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
        var world = new GameRoom();
        Assert.True(world.TryJoin(10, out _, out _));
        Assert.True(world.ApplyMovementInput(
            10,
            new MovementInputPacket(1, -1, 0)));

        world.Update(10f);
        Assert.True(world.TryGetPlayer(10, out var player));

        Assert.Equal(GameProtocol.PlayerSize / 2f, player.X);
    }

    [Fact]
    public void LeavingPlayerReleasesItsSlot()
    {
        var world = new GameRoom();
        Assert.True(world.TryJoin(10, out var firstId, out _));

        Assert.True(world.Leave(10));
        Assert.False(world.Leave(10));
        Assert.True(world.TryJoin(20, out var reusedId, out _));

        Assert.Equal(firstId, reusedId);
    }

    [Fact]
    public void OutOfRangeInputIsRejectedAtWorldBoundary()
    {
        var world = new GameRoom();
        Assert.True(world.TryJoin(10, out _, out _));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            world.ApplyMovementInput(
                10,
                new MovementInputPacket(1, 2, 0)));
    }

    [Fact]
    public void JumpUsesServerGravityAndLandsOnGround()
    {
        var world = new GameRoom();
        Assert.True(world.TryJoin(10, out _, out _));
        Assert.True(world.ApplyActionCommand(
            10,
            new ActionCommandPacket(1, ActionId.Jump)));

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
        var secondPressWorld = new GameRoom();
        var controlWorld = new GameRoom();
        Assert.True(secondPressWorld.TryJoin(10, out _, out _));
        Assert.True(controlWorld.TryJoin(10, out _, out _));

        ApplyJumpToBoth(1);
        UpdateBoth();
        Assert.True(secondPressWorld.ApplyActionCommand(
            10,
            new ActionCommandPacket(2, ActionId.Jump)));
        UpdateBoth();

        Assert.True(secondPressWorld.TryGetPlayer(10, out var secondPressPlayer));
        Assert.True(controlWorld.TryGetPlayer(10, out var controlPlayer));
        Assert.Equal(controlPlayer.Z, secondPressPlayer.Z, precision: 4);

        void ApplyJumpToBoth(uint sequence)
        {
            var command = new ActionCommandPacket(sequence, ActionId.Jump);
            Assert.True(secondPressWorld.ApplyActionCommand(10, command));
            Assert.True(controlWorld.ApplyActionCommand(10, command));
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
        var world = new GameRoom();
        Assert.True(world.TryJoin(10, out _, out _));
        Assert.True(world.ApplyMovementInput(
            10,
            new MovementInputPacket(1, -1, 0)));
        Assert.True(world.ApplyActionCommand(
            10,
            new ActionCommandPacket(1, ActionId.BasicAttack)));

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
        Assert.True(world.ApplyActionCommand(
            10,
            new ActionCommandPacket(1, ActionId.BasicAttack)));

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
        Assert.True(world.ApplyActionCommand(
            10,
            new ActionCommandPacket(1, ActionId.BasicAttack)));
        world.Update(TickSeconds);

        Assert.True(world.ApplyMovementInput(
            20,
            new MovementInputPacket(3, -1, 0)));
        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(20, out var firstKnockbackTick));
        Assert.True(firstKnockbackTick.X > beforeHit.X);

        Assert.True(world.ApplyMovementInput(
            20,
            new MovementInputPacket(4, 0, 0)));
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
    public void HitStunDiscardsActionsAndAcceptsNewCommandsAfterRecovery()
    {
        var world = CreateWorldWithPlayersInAttackRange();
        Assert.True(world.TryGetPlayer(20, out var beforeHit));
        Assert.True(world.ApplyActionCommand(
            10,
            new ActionCommandPacket(1, ActionId.BasicAttack)));
        world.Update(TickSeconds);

        Assert.True(world.ApplyMovementInput(
            20,
            new MovementInputPacket(3, -1, 1)));
        Assert.True(world.ApplyActionCommand(
            20,
            new ActionCommandPacket(1, ActionId.BasicAttack)));
        Assert.True(world.ApplyActionCommand(
            20,
            new ActionCommandPacket(2, ActionId.Jump)));
        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(20, out var stunned));
        Assert.Equal(beforeHit.Y, stunned.Y);
        Assert.Equal(0f, stunned.Z);
        Assert.False(stunned.IsAttacking);
        Assert.Empty(world.CreateSnapshot().Arrows);

        var stunTicks = (int)MathF.Ceiling(
            GameProtocol.MeleeHitStunDuration / TickSeconds) + 1;
        for (var index = 1; index < stunTicks; index++)
        {
            world.Update(TickSeconds);
        }

        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(20, out var noRepeatedInput));
        Assert.Equal(0f, noRepeatedInput.Z);
        Assert.False(noRepeatedInput.IsAttacking);
        Assert.Empty(world.CreateSnapshot().Arrows);

        Assert.True(world.ApplyActionCommand(
            20,
            new ActionCommandPacket(3, ActionId.Jump)));
        Assert.True(world.ApplyActionCommand(
            20,
            new ActionCommandPacket(4, ActionId.BasicAttack)));
        world.Update(TickSeconds);

        Assert.True(world.TryGetPlayer(20, out var recovered));
        Assert.True(recovered.Z > 0f);
        Assert.True(recovered.IsAttacking);
        Assert.Single(world.CreateSnapshot().Arrows);
    }

    [Fact]
    public void InvulnerabilityStateExpiresAfterServerDuration()
    {
        var world = CreateWorldWithPlayersInAttackRange();
        Assert.True(world.ApplyActionCommand(
            10,
            new ActionCommandPacket(1, ActionId.BasicAttack)));
        world.Update(TickSeconds);

        Assert.True(world.TryGetPlayer(20, out var protectedPlayer));
        Assert.True(protectedPlayer.IsInvulnerable);

        var protectedTicks = (int)MathF.Ceiling(
            GameProtocol.HitInvulnerabilityDuration / TickSeconds) - 1;
        for (var index = 0; index < protectedTicks; index++)
        {
            world.Update(TickSeconds);
        }

        Assert.True(world.TryGetPlayer(20, out var stillProtected));
        Assert.True(stillProtected.IsInvulnerable);

        world.Update(TickSeconds);
        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(20, out var expired));
        Assert.False(expired.IsInvulnerable);
    }

    [Fact]
    public void ArrowKnockbackIsWeakerAndFollowsArrowDirection()
    {
        var world = CreateWorldWithPlayersInAttackRange();
        Assert.True(world.TryGetPlayer(10, out var beforeHit));
        Assert.True(world.ApplyActionCommand(
            20,
            new ActionCommandPacket(1, ActionId.BasicAttack)));
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
        var world = new GameRoom();
        Assert.True(world.TryJoin(10, out _, out _));
        Assert.True(world.TryJoin(20, out _, out _));
        Assert.True(world.ApplyMovementInput(
            10,
            new MovementInputPacket(1, 1, 0)));
        Assert.True(world.ApplyMovementInput(
            20,
            new MovementInputPacket(1, 1, 0)));
        for (var index = 0; index < 100; index++)
        {
            world.Update(TickSeconds);
        }

        Assert.True(world.ApplyMovementInput(
            10,
            new MovementInputPacket(2, 0, 0)));
        Assert.True(world.ApplyMovementInput(
            20,
            new MovementInputPacket(2, 0, 0)));
        world.Update(TickSeconds);
        Assert.True(world.ApplyActionCommand(
            10,
            new ActionCommandPacket(1, ActionId.BasicAttack)));
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
        Assert.True(world.ApplyMovementInput(
            20,
            new MovementInputPacket(3, 0, 1)));
        for (var index = 0; index < 5; index++)
        {
            world.Update(TickSeconds);
        }

        Assert.True(world.ApplyActionCommand(
            10,
            new ActionCommandPacket(1, ActionId.BasicAttack)));
        world.Update(TickSeconds);

        Assert.True(world.TryGetPlayer(20, out var target));
        Assert.Equal(GameProtocol.MaxHealth, target.Health);
    }

    [Fact]
    public void MeleeAndRangedAttacksResolveWithDifferentDamageTiming()
    {
        var world = CreateWorldWithPlayersInAttackRange();
        Assert.True(world.ApplyActionCommand(
            10,
            new ActionCommandPacket(1, ActionId.BasicAttack)));
        Assert.True(world.ApplyActionCommand(
            20,
            new ActionCommandPacket(1, ActionId.BasicAttack)));

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
        Assert.True(world.ApplyMovementInput(
            10,
            new MovementInputPacket(3, 0, 1)));
        for (var index = 0; index < 5; index++)
        {
            world.Update(TickSeconds);
        }

        Assert.True(world.ApplyMovementInput(
            10,
            new MovementInputPacket(4, 0, 0)));
        Assert.True(world.ApplyActionCommand(
            20,
            new ActionCommandPacket(1, ActionId.BasicAttack)));
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
        Assert.False(dead.IsInvulnerable);

        Assert.True(world.ApplyMovementInput(
            20,
            new MovementInputPacket(3, 1, 1)));
        Assert.True(world.ApplyActionCommand(
            20,
            new ActionCommandPacket(1, ActionId.BasicAttack)));
        Assert.True(world.ApplyActionCommand(
            20,
            new ActionCommandPacket(2, ActionId.Jump)));
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

        Assert.True(world.ApplyActionCommand(
            20,
            new ActionCommandPacket(1, ActionId.Revive)));
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
    public void ReviveRequiresNewCommandAfterDelayAndRestoresPlayer()
    {
        var world = CreateWorldWithPlayersInAttackRange();
        KillSecondPlayer(world);
        Assert.True(world.TryGetPlayer(20, out var dead));

        uint actionSequence = 1;
        Assert.True(world.ApplyActionCommand(
            20,
            new ActionCommandPacket(actionSequence++, ActionId.Revive)));
        world.Update(TickSeconds);

        var requiredTicks = (int)(
            GameProtocol.ReviveDelaySeconds * GameProtocol.SimulationRate);
        for (var index = 0; index < requiredTicks; index++)
        {
            world.Update(TickSeconds);
        }

        Assert.True(world.TryGetPlayer(20, out var stillDead));
        Assert.True(stillDead.IsDead);

        Assert.True(world.ApplyActionCommand(
            20,
            new ActionCommandPacket(actionSequence++, ActionId.Revive)));
        world.Update(TickSeconds);

        Assert.True(world.TryGetPlayer(20, out var revived));
        Assert.False(revived.IsDead);
        Assert.Equal(GameProtocol.MaxHealth, revived.Health);
        Assert.Equal(dead.X, revived.X);
        Assert.Equal(dead.Y, revived.Y);

        Assert.True(world.ApplyActionCommand(
            20,
            new ActionCommandPacket(actionSequence++, ActionId.BasicAttack)));
        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(20, out var attackingAfterRevive));
        Assert.True(attackingAfterRevive.IsAttacking);

        Assert.True(world.ApplyMovementInput(
            20,
            new MovementInputPacket(3, -1, 0)));
        Assert.True(world.ApplyActionCommand(
            20,
            new ActionCommandPacket(actionSequence++, ActionId.Jump)));
        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(20, out var active));
        Assert.True(active.X < revived.X);
        Assert.True(active.Z > 0f);
    }

    [Fact]
    public void MeleeSkillDashesDamagesAndStartsCooldown()
    {
        var world = CreateWorldWithPlayersInAttackRange();
        Assert.True(world.TryGetPlayer(10, out var beforeSkill));
        Assert.True(world.ApplyActionCommand(
            10,
            new ActionCommandPacket(1, ActionId.WarriorDashSlash)));

        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(10, out var player));
        Assert.True(world.TryGetPlayer(20, out var target));

        Assert.Equal(
            beforeSkill.X + GameProtocol.MeleeSkillDashDistance,
            player.X);
        Assert.Equal(
            GameProtocol.MaxHealth - GameProtocol.MeleeSkillDamage,
            target.Health);
        Assert.True(player.IsAttacking);
        Assert.InRange(
            player.SkillCooldownRemaining,
            GameProtocol.SkillCooldown - TickSeconds,
            GameProtocol.SkillCooldown);
    }

    [Fact]
    public void SkillCannotBeRepeatedBeforeCooldownExpires()
    {
        var world = new GameRoom();
        Assert.True(world.TryJoin(10, out _, out _));
        Assert.True(world.ApplyActionCommand(
            10,
            new ActionCommandPacket(1, ActionId.WarriorDashSlash)));
        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(10, out var afterFirstSkill));

        Assert.True(world.ApplyActionCommand(
            10,
            new ActionCommandPacket(2, ActionId.WarriorDashSlash)));
        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(10, out var afterSecondRequest));

        Assert.Equal(afterFirstSkill.X, afterSecondRequest.X);
        Assert.True(afterSecondRequest.SkillCooldownRemaining > 0f);

        var cooldownTicks = (int)(
            GameProtocol.SkillCooldown * GameProtocol.SimulationRate);
        for (var index = 0; index < cooldownTicks; index++)
        {
            world.Update(TickSeconds);
        }

        Assert.True(world.ApplyActionCommand(
            10,
            new ActionCommandPacket(3, ActionId.WarriorDashSlash)));
        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(10, out var afterCooldown));
        Assert.Equal(
            afterSecondRequest.X + GameProtocol.MeleeSkillDashDistance,
            afterCooldown.X);
    }

    [Fact]
    public void RangerSkillArrowHasExtendedPropertiesAndDealsSkillDamage()
    {
        var world = new GameRoom();
        Assert.True(world.TryJoin(10, out _, out _));
        Assert.True(world.TryJoin(20, out _, out _));
        Assert.True(world.ApplyMovementInput(
            20,
            new MovementInputPacket(1, -1, 0)));
        world.Update(TickSeconds);
        Assert.True(world.ApplyMovementInput(
            20,
            new MovementInputPacket(2, 0, 0)));
        world.Update(TickSeconds);
        Assert.True(world.ApplyActionCommand(
            20,
            new ActionCommandPacket(1, ActionId.RangerPowerArrow)));

        world.Update(TickSeconds);
        var arrow = Assert.Single(world.CreateSnapshot().Arrows);
        Assert.True(arrow.IsSkillArrow);
        Assert.Equal(GameProtocol.RangerPlayerId, arrow.OwnerPlayerId);

        var maximumTicks = (int)MathF.Ceiling(
            GameProtocol.RangerSkillArrowMaxDistance
                / (GameProtocol.RangerSkillArrowSpeed * TickSeconds));
        for (var index = 1; index < maximumTicks; index++)
        {
            world.Update(TickSeconds);
            if (world.CreateSnapshot().Arrows.Length == 0)
            {
                break;
            }
        }

        Assert.True(world.TryGetPlayer(10, out var target));
        Assert.Equal(
            GameProtocol.MaxHealth - GameProtocol.RangerSkillDamage,
            target.Health);
        Assert.Empty(world.CreateSnapshot().Arrows);

        Assert.True(GameProtocol.RangerSkillArrowSpeed > GameProtocol.ArrowSpeed);
        Assert.True(GameProtocol.RangerSkillArrowMaxDistance > GameProtocol.ArrowMaxDistance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameTickSkillsResolveRegardlessOfPlayerStorageOrder(
        bool rangerStoredFirst)
    {
        var setup = CreateLethalSameTickSkillWorld(rangerStoredFirst);
        Assert.True(setup.World.ApplyActionCommand(
            setup.WarriorConnectionId,
            new ActionCommandPacket(5, ActionId.WarriorDashSlash)));
        Assert.True(setup.World.ApplyActionCommand(
            setup.RangerConnectionId,
            new ActionCommandPacket(1, ActionId.RangerPowerArrow)));

        setup.World.Update(TickSeconds);

        Assert.True(setup.World.TryGetPlayer(
            setup.RangerConnectionId,
            out var ranger));
        Assert.True(ranger.IsDead);
        var arrow = Assert.Single(setup.World.CreateSnapshot().Arrows);
        Assert.Equal(GameProtocol.RangerPlayerId, arrow.OwnerPlayerId);
        Assert.True(arrow.IsSkillArrow);
    }

    private static GameRoom CreateWorldWithPlayersInAttackRange()
    {
        var world = new GameRoom();
        Assert.True(world.TryJoin(10, out _, out _));
        Assert.True(world.TryJoin(20, out _, out _));
        MovePlayersIntoAttackRange(world, 10, 20);
        return world;
    }

    private static (
        GameRoom World,
        long WarriorConnectionId,
        long RangerConnectionId) CreateLethalSameTickSkillWorld(
            bool rangerStoredFirst)
    {
        const long initialWarriorConnectionId = 10;
        const long rangerConnectionId = 20;
        var warriorConnectionId = initialWarriorConnectionId;
        var world = new GameRoom();
        Assert.True(world.TryJoin(initialWarriorConnectionId, out _, out _));
        Assert.True(world.TryJoin(rangerConnectionId, out _, out _));
        if (rangerStoredFirst)
        {
            Assert.True(world.Leave(initialWarriorConnectionId));
            warriorConnectionId = 30;
            Assert.True(world.TryJoin(
                warriorConnectionId,
                out var reassignedPlayerId,
                out _));
            Assert.Equal(1, reassignedPlayerId);
        }

        MovePlayersIntoAttackRange(
            world,
            warriorConnectionId,
            rangerConnectionId);
        DamageRangerToLethalSkillRange(
            world,
            warriorConnectionId,
            rangerConnectionId);
        return (world, warriorConnectionId, rangerConnectionId);
    }

    private static void MovePlayersIntoAttackRange(
        GameRoom world,
        long warriorConnectionId,
        long rangerConnectionId)
    {
        Assert.True(world.ApplyMovementInput(
            warriorConnectionId,
            new MovementInputPacket(1, 1, 0)));
        Assert.True(world.ApplyMovementInput(
            rangerConnectionId,
            new MovementInputPacket(1, -1, 0)));
        for (var index = 0; index < 30; index++)
        {
            world.Update(TickSeconds);
        }

        Assert.True(world.ApplyMovementInput(
            warriorConnectionId,
            new MovementInputPacket(2, 0, 0)));
        Assert.True(world.ApplyMovementInput(
            rangerConnectionId,
            new MovementInputPacket(2, 0, 0)));
        world.Update(TickSeconds);
    }

    private static void DamageRangerToLethalSkillRange(
        GameRoom world,
        long warriorConnectionId,
        long rangerConnectionId)
    {
        uint actionSequence = 1;
        uint movementSequence = 3;
        var requiredDamage = GameProtocol.MaxHealth - GameProtocol.MeleeSkillDamage;
        var attackCount = (requiredDamage + GameProtocol.AttackDamage - 1)
            / GameProtocol.AttackDamage;
        for (var attackIndex = 0; attackIndex < attackCount; attackIndex++)
        {
            Assert.True(world.ApplyActionCommand(
                warriorConnectionId,
                new ActionCommandPacket(
                    actionSequence++,
                    ActionId.BasicAttack)));
            world.Update(TickSeconds);

            Assert.True(world.ApplyMovementInput(
                warriorConnectionId,
                new MovementInputPacket(movementSequence++, 1, 0)));
            for (var followTick = 0; followTick < 4; followTick++)
            {
                world.Update(TickSeconds);
            }

            Assert.True(world.ApplyMovementInput(
                warriorConnectionId,
                new MovementInputPacket(movementSequence++, 0, 0)));
            for (var recoveryTick = 0; recoveryTick < 3; recoveryTick++)
            {
                world.Update(TickSeconds);
            }
        }

        Assert.True(world.TryGetPlayer(rangerConnectionId, out var ranger));
        Assert.InRange(ranger.Health, 1, GameProtocol.MeleeSkillDamage);
    }

    private static void KillSecondPlayer(GameRoom world)
    {
        uint sequence = 3;
        var attackCount = GameProtocol.MaxHealth / GameProtocol.AttackDamage;
        for (var attackIndex = 0; attackIndex < attackCount; attackIndex++)
        {
            Assert.True(world.ApplyActionCommand(
                10,
                new ActionCommandPacket(sequence++, ActionId.BasicAttack)));
            world.Update(TickSeconds);
            if (attackIndex == attackCount - 1)
            {
                continue;
            }

            Assert.True(world.ApplyMovementInput(
                10,
                new MovementInputPacket(sequence++, 1, 0)));
            for (var followTick = 0; followTick < 4; followTick++)
            {
                world.Update(TickSeconds);
            }

            Assert.True(world.ApplyMovementInput(
                10,
                new MovementInputPacket(sequence++, 0, 0)));
            for (var cooldownTick = 4; cooldownTick < 7; cooldownTick++)
            {
                world.Update(TickSeconds);
            }
        }
    }
}
