namespace ActionGame.Contracts.Protocol;

public static class GameProtocol
{
    public const byte Version = 1;
    public const int MaxPlayers = 2;
    public const float WorldWidth = 800f;
    public const float WorldHeight = 450f;
    public const float PlayerSize = 28f;
    public const float PlayerSpeed = 180f;
    public const int SimulationRate = 20;
    public const int DefaultPort = 7777;
}

public enum PacketType : byte
{
    JoinRequest = 1,
    JoinAccepted = 2,
    JoinRejected = 3,
    InputCommand = 4,
    WorldSnapshot = 5,
    LeaveRequest = 6,
}

public enum JoinRejectReason : byte
{
    ServerFull = 1,
    AlreadyJoined = 2,
}

public readonly record struct JoinAcceptedPacket(int PlayerId);

public readonly record struct JoinRejectedPacket(JoinRejectReason Reason);

public readonly record struct InputCommandPacket(
    uint Sequence,
    sbyte Horizontal,
    sbyte Vertical);

public readonly record struct PlayerSnapshot(int PlayerId, float X, float Y);

public sealed record WorldSnapshotPacket(long ServerTick, PlayerSnapshot[] Players);

