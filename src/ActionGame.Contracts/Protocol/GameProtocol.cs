namespace ActionGame.Contracts.Protocol;

public static class GameProtocol
{
    public const byte Version = 5;
    public const int MaxPlayers = 2;
    public const int RangerPlayerId = 2;
    public const int MaxArrows = 16;
    public const float WorldWidth = 800f;
    public const float WorldHeight = 450f;
    public const float PlayerSize = 28f;
    public const float FloorTop = 120f;
    public const float FloorBottom = 420f;
    public const float HorizontalSpeed = 180f;
    public const float DepthSpeed = 120f;
    public const float JumpInitialVelocity = 420f;
    public const float Gravity = 1100f;
    public const float AttackDuration = 0.18f;
    public const float AttackCooldown = 0.30f;
    public const float AttackReach = 42f;
    public const float AttackDepthTolerance = 24f;
    public const float AttackHeightTolerance = 48f;
    public const int MaxHealth = 100;
    public const int AttackDamage = 20;
    public const int RangerAttackDamage = 15;
    public const float ArrowSpeed = 480f;
    public const float ArrowMaxDistance = 360f;
    public const float ArrowSpawnHeight = 28f;
    public const float ArrowDepthTolerance = 18f;
    public const float ArrowHeightTolerance = 32f;
    public const float ReviveDelaySeconds = 5f;
    public const int SimulationRate = 20;
    public const int DefaultPort = 7777;
}

[Flags]
public enum InputActionFlags : byte
{
    None = 0,
    Attack = 1 << 0,
    Jump = 1 << 1,
    ReservedZ = 1 << 2,
    Revive = 1 << 3,
}

public enum FacingDirection : sbyte
{
    Left = -1,
    Right = 1,
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
    sbyte Depth,
    InputActionFlags Actions);

public readonly record struct PlayerSnapshot(
    int PlayerId,
    float X,
    float Y,
    float Z,
    FacingDirection Facing,
    bool IsAttacking,
    int Health,
    bool IsDead);

public readonly record struct ArrowSnapshot(
    int ArrowId,
    int OwnerPlayerId,
    float X,
    float Y,
    float Z,
    FacingDirection Direction);

public sealed record WorldSnapshotPacket(
    long ServerTick,
    PlayerSnapshot[] Players,
    ArrowSnapshot[] Arrows);
