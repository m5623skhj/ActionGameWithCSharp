using ActionGame.Contracts.Protocol;

namespace ActionGame.Server.Game;

/// <summary>
/// Owns game rooms and routes each connection to its joined room.
/// All methods are called from the server's single event thread.
/// </summary>
public sealed class GameWorld
{
    public const int DefaultRoomId = 1;

    private readonly Dictionary<int, GameRoom> rooms = [];
    private readonly Dictionary<long, int> roomIdsByConnection = [];
    private int nextRoomId = DefaultRoomId;

    public GameWorld()
    {
        var roomId = CreateRoom();
        if (roomId != DefaultRoomId)
        {
            throw new InvalidOperationException("The default room ID is invalid.");
        }
    }

    public int RoomCount => rooms.Count;

    public IEnumerable<int> RoomIds => rooms.Keys;

    public int CreateRoom()
    {
        var roomId = nextRoomId++;
        rooms.Add(roomId, new GameRoom());
        return roomId;
    }

    public bool TryRemoveRoom(int roomId)
    {
        if (roomId == DefaultRoomId
            || !rooms.TryGetValue(roomId, out var room)
            || room.PlayerCount > 0)
        {
            return false;
        }

        return rooms.Remove(roomId);
    }

    public bool TryGetRoom(int roomId, out GameRoom room)
    {
        return rooms.TryGetValue(roomId, out room!);
    }

    public bool TryGetRoomId(long connectionId, out int roomId)
    {
        return roomIdsByConnection.TryGetValue(connectionId, out roomId);
    }

    public bool TryJoinRoom(
        long connectionId,
        int roomId,
        out int playerId,
        out RoomJoinFailure failure)
    {
        if (roomIdsByConnection.ContainsKey(connectionId))
        {
            playerId = 0;
            failure = RoomJoinFailure.AlreadyJoined;
            return false;
        }

        if (!rooms.TryGetValue(roomId, out var room))
        {
            playerId = 0;
            failure = RoomJoinFailure.RoomNotFound;
            return false;
        }

        if (!room.TryJoin(connectionId, out playerId, out var rejectionReason))
        {
            failure = rejectionReason == JoinRejectReason.AlreadyJoined
                ? RoomJoinFailure.AlreadyJoined
                : RoomJoinFailure.RoomFull;
            return false;
        }

        roomIdsByConnection.Add(connectionId, roomId);
        failure = RoomJoinFailure.None;
        return true;
    }

    public bool Leave(
        long connectionId,
        out int roomId,
        out int playerId)
    {
        if (!roomIdsByConnection.Remove(connectionId, out roomId)
            || !rooms.TryGetValue(roomId, out var room))
        {
            playerId = 0;
            return false;
        }

        playerId = room.TryGetPlayer(connectionId, out var player)
            ? player.PlayerId
            : 0;
        room.Leave(connectionId);
        return true;
    }

    public bool ApplyMovementInput(
        long connectionId,
        MovementInputPacket input)
    {
        return TryGetJoinedRoom(connectionId, out var room)
            && room.ApplyMovementInput(connectionId, input);
    }

    public bool ApplyActionCommand(
        long connectionId,
        ActionCommandPacket command)
    {
        return TryGetJoinedRoom(connectionId, out var room)
            && room.ApplyActionCommand(connectionId, command);
    }

    public bool TryGetPlayer(long connectionId, out PlayerSnapshot player)
    {
        if (TryGetJoinedRoom(connectionId, out var room))
        {
            return room.TryGetPlayer(connectionId, out player);
        }

        player = default;
        return false;
    }

    public void Update(float deltaSeconds)
    {
        foreach (var room in rooms.Values)
        {
            room.Update(deltaSeconds);
        }
    }

    private bool TryGetJoinedRoom(long connectionId, out GameRoom room)
    {
        if (roomIdsByConnection.TryGetValue(connectionId, out var roomId))
        {
            return rooms.TryGetValue(roomId, out room!);
        }

        room = null!;
        return false;
    }
}

public enum RoomJoinFailure : byte
{
    None = 0,
    RoomNotFound = 1,
    AlreadyJoined = 2,
    RoomFull = 3,
}
