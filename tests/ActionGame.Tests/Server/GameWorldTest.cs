using ActionGame.Contracts.Protocol;
using ActionGame.Server.Game;

namespace ActionGame.Tests.Server;

public sealed class GameWorldTest
{
    private const float TickSeconds = 1f / GameProtocol.SimulationRate;

    [Fact]
    public void ConstructorCreatesDefaultRoom()
    {
        var world = new GameWorld();

        Assert.Equal(1, world.RoomCount);
        Assert.True(world.TryGetRoom(GameWorld.DefaultRoomId, out var room));
        Assert.Equal(0, room.PlayerCount);
    }

    [Fact]
    public void RoomsKeepPlayersAndSnapshotsIsolated()
    {
        var world = new GameWorld();
        var secondRoomId = world.CreateRoom();
        Assert.True(world.TryJoinRoom(
            10,
            GameWorld.DefaultRoomId,
            out var firstPlayerId,
            out var firstFailure));
        Assert.True(world.TryJoinRoom(
            20,
            secondRoomId,
            out var secondPlayerId,
            out var secondFailure));

        Assert.Equal(RoomJoinFailure.None, firstFailure);
        Assert.Equal(RoomJoinFailure.None, secondFailure);
        Assert.Equal(firstPlayerId, secondPlayerId);
        Assert.True(world.ApplyMovementInput(
            10,
            new MovementInputPacket(1, 1, 0)));
        world.Update(TickSeconds);

        Assert.True(world.TryGetPlayer(10, out var movedPlayer));
        Assert.True(world.TryGetPlayer(20, out var stationaryPlayer));
        Assert.True(movedPlayer.X > stationaryPlayer.X);
        Assert.True(world.TryGetRoom(GameWorld.DefaultRoomId, out var firstRoom));
        Assert.True(world.TryGetRoom(secondRoomId, out var secondRoom));
        Assert.Single(firstRoom.CreateSnapshot().Players);
        Assert.Single(secondRoom.CreateSnapshot().Players);
    }

    [Fact]
    public void ConnectionCannotJoinMoreThanOneRoom()
    {
        var world = new GameWorld();
        var secondRoomId = world.CreateRoom();
        Assert.True(world.TryJoinRoom(
            10,
            GameWorld.DefaultRoomId,
            out _,
            out _));

        Assert.False(world.TryJoinRoom(
            10,
            secondRoomId,
            out _,
            out var failure));

        Assert.Equal(RoomJoinFailure.AlreadyJoined, failure);
        Assert.True(world.TryGetRoomId(10, out var joinedRoomId));
        Assert.Equal(GameWorld.DefaultRoomId, joinedRoomId);
    }

    [Fact]
    public void FullRoomDoesNotPreventJoiningAnotherRoom()
    {
        var world = new GameWorld();
        Assert.True(world.TryJoinRoom(
            10,
            GameWorld.DefaultRoomId,
            out _,
            out _));
        Assert.True(world.TryJoinRoom(
            20,
            GameWorld.DefaultRoomId,
            out _,
            out _));
        Assert.False(world.TryJoinRoom(
            30,
            GameWorld.DefaultRoomId,
            out _,
            out var fullFailure));

        var secondRoomId = world.CreateRoom();
        Assert.True(world.TryJoinRoom(
            30,
            secondRoomId,
            out var playerId,
            out var secondRoomFailure));

        Assert.Equal(RoomJoinFailure.RoomFull, fullFailure);
        Assert.Equal(RoomJoinFailure.None, secondRoomFailure);
        Assert.Equal(1, playerId);
    }

    [Fact]
    public void OnlyEmptyNonDefaultRoomCanBeRemoved()
    {
        var world = new GameWorld();
        var secondRoomId = world.CreateRoom();
        Assert.True(world.TryJoinRoom(
            10,
            secondRoomId,
            out var playerId,
            out _));

        Assert.False(world.TryRemoveRoom(secondRoomId));
        Assert.True(world.Leave(10, out var leftRoomId, out var leftPlayerId));
        Assert.Equal(secondRoomId, leftRoomId);
        Assert.Equal(playerId, leftPlayerId);
        Assert.True(world.TryRemoveRoom(secondRoomId));
        Assert.False(world.TryRemoveRoom(GameWorld.DefaultRoomId));
        Assert.False(world.TryGetRoom(secondRoomId, out _));
    }

    [Fact]
    public void JoiningUnknownRoomIsRejected()
    {
        var world = new GameWorld();

        Assert.False(world.TryJoinRoom(
            10,
            999,
            out _,
            out var failure));

        Assert.Equal(RoomJoinFailure.RoomNotFound, failure);
        Assert.False(world.TryGetRoomId(10, out _));
    }
}
