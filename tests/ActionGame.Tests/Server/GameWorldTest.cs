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

        Assert.True(world.ApplyInput(10, new InputCommandPacket(2, 1, 0)));
        Assert.False(world.ApplyInput(10, new InputCommandPacket(1, -1, 0)));
        world.Update(TickSeconds);
        Assert.True(world.TryGetPlayer(10, out var moved));

        Assert.Equal(
            initial.X + (GameProtocol.PlayerSpeed * TickSeconds),
            moved.X,
            precision: 4);
        Assert.Equal(initial.Y, moved.Y);
    }

    [Fact]
    public void DiagonalInputIsNormalized()
    {
        var horizontalWorld = new GameWorld();
        var diagonalWorld = new GameWorld();
        Assert.True(horizontalWorld.TryJoin(10, out _, out _));
        Assert.True(diagonalWorld.TryJoin(10, out _, out _));
        Assert.True(horizontalWorld.ApplyInput(10, new InputCommandPacket(1, 1, 0)));
        Assert.True(diagonalWorld.ApplyInput(10, new InputCommandPacket(1, 1, 1)));
        Assert.True(horizontalWorld.TryGetPlayer(10, out var initial));

        horizontalWorld.Update(TickSeconds);
        diagonalWorld.Update(TickSeconds);
        Assert.True(horizontalWorld.TryGetPlayer(10, out var horizontal));
        Assert.True(diagonalWorld.TryGetPlayer(10, out var diagonal));

        var horizontalDistance = horizontal.X - initial.X;
        var diagonalDistance = MathF.Sqrt(
            MathF.Pow(diagonal.X - initial.X, 2f)
            + MathF.Pow(diagonal.Y - initial.Y, 2f));
        Assert.Equal(horizontalDistance, diagonalDistance, precision: 4);
    }

    [Fact]
    public void PlayerPositionIsClampedInsideWorld()
    {
        var world = new GameWorld();
        Assert.True(world.TryJoin(10, out _, out _));
        Assert.True(world.ApplyInput(10, new InputCommandPacket(1, -1, 0)));

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
            world.ApplyInput(10, new InputCommandPacket(1, 2, 0)));
    }
}
