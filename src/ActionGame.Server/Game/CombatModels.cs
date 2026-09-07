using ActionGame.Contracts.Protocol;

namespace ActionGame.Server.Game;

internal readonly record struct PendingSkill(PlayerState Player, ActionId ActionId);

internal readonly record struct SkillPlayerSnapshot(
    float X,
    float Y,
    float Z,
    FacingDirection Facing,
    bool IsDead);

internal readonly record struct SkillIntent(
    PlayerState Player,
    ActionId ActionId,
    float X,
    float Y,
    float Z,
    FacingDirection Facing);

internal readonly record struct PendingHit(int Damage, float KnockbackVelocityX);

internal readonly record struct PendingSkillHit(
    int Damage,
    float KnockbackVelocityX,
    float HitStunDuration);
