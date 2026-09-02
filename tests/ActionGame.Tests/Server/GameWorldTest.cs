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
    public void SimultaneousAttacksDamageBothPlayers()
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
        Assert.Equal(GameProtocol.MaxHealth - GameProtocol.AttackDamage, first.Health);
        Assert.Equal(GameProtocol.MaxHealth - GameProtocol.AttackDamage, second.Health);
    }

    [Fact]
    public void ZeroHealthMarksPlayerDeadAndIgnoresFurtherInput()
    {
        var world = CreateWorldWithPlayersInAttackRange();
        uint sequence = 3;
        for (var attackIndex = 0;
            attackIndex < GameProtocol.MaxHealth / GameProtocol.AttackDamage;
            attackIndex++)
        {
            Assert.True(world.ApplyInput(
                10,
                new InputCommandPacket(
                    sequence++,
                    0,
                    0,
                    InputActionFlags.Attack)));
            world.Update(TickSeconds);
            Assert.True(world.ApplyInput(
                10,
                new InputCommandPacket(
                    sequence++,
                    0,
                    0,
                    InputActionFlags.None)));
            for (var cooldownTick = 0; cooldownTick < 7; cooldownTick++)
            {
                world.Update(TickSeconds);
            }
        }

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
}
