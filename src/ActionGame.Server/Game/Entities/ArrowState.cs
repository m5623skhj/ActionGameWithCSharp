using ActionGame.Contracts.Protocol;

namespace ActionGame.Server.Game;

internal sealed class ArrowState(
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
