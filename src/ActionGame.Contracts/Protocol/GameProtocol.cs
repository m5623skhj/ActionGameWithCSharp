namespace ActionGame.Contracts.Protocol;

public static class GameProtocol
{
    public const byte Version = 9;
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
    public const float MeleeKnockbackSpeed = 240f;
    public const float ArrowKnockbackSpeed = 140f;
    public const float KnockbackDeceleration = 900f;
    public const float MeleeHitStunDuration = 0.25f;
    public const float ArrowHitStunDuration = 0.12f;
    public const float HitInvulnerabilityDuration = 0.30f;
    public const float SkillCooldown = 4f;
    public const float SkillCommandWindow = 0.6f;
    public const float MeleeSkillDuration = 0.28f;
    public const float MeleeSkillDashDistance = 100f;
    public const int MeleeSkillDamage = 30;
    public const float MeleeSkillKnockbackSpeed = 360f;
    public const float MeleeSkillHitStunDuration = 0.35f;
    public const float ArrowSpeed = 480f;
    public const float ArrowMaxDistance = 360f;
    public const float ArrowSpawnHeight = 28f;
    public const float ArrowDepthTolerance = 18f;
    public const float ArrowHeightTolerance = 32f;
    public const int RangerSkillDamage = 25;
    public const float RangerSkillArrowSpeed = 720f;
    public const float RangerSkillArrowMaxDistance = 600f;
    public const float RangerSkillKnockbackSpeed = 260f;
    public const float RangerSkillHitStunDuration = 0.25f;
    public const int ReviveDelaySeconds = 5;
    public const int SimulationRate = 20;
    public const int DefaultPort = 7777;
}

public enum ActionId : ushort
{
    BasicAttack = 1,
    Jump = 2,
    Revive = 3,
    WarriorDashSlash = 100,
    RangerPowerArrow = 200,
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
    MovementInput = 4,
    WorldSnapshot = 5,
    LeaveRequest = 6,
    ActionCommand = 7,
}

public enum JoinRejectReason : byte
{
    ServerFull = 1,
    AlreadyJoined = 2,
}

public readonly record struct JoinAcceptedPacket(int PlayerId);

public readonly record struct JoinRejectedPacket(JoinRejectReason Reason);

public readonly record struct MovementInputPacket(
    uint Sequence,
    sbyte Horizontal,
    sbyte Depth);

public readonly record struct ActionCommandPacket(
    uint Sequence,
    ActionId ActionId);

public readonly record struct PlayerSnapshot(
    int PlayerId,
    float X,
    float Y,
    float Z,
    FacingDirection Facing,
    bool IsAttacking,
    int Health,
    bool IsDead,
    byte ReviveSecondsRemaining,
    bool IsInvulnerable,
    float SkillCooldownRemaining);

public readonly record struct ArrowSnapshot(
    int ArrowId,
    int OwnerPlayerId,
    float X,
    float Y,
    float Z,
    FacingDirection Direction,
    bool IsSkillArrow);

public sealed record WorldSnapshotPacket(
    long ServerTick,
    PlayerSnapshot[] Players,
    ArrowSnapshot[] Arrows);
