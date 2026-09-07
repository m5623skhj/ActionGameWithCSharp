using ActionGame.Contracts.Protocol;

namespace ActionGame.Server.Game;

internal sealed class DamageSystem(RoomState state)
{
    public void ApplyDamage(
        PlayerState player,
        int damage,
        float knockbackVelocityX,
        float hitStunDuration)
    {
        if (player.InvulnerabilityTimeRemaining > 0f)
        {
            return;
        }

        player.Health = Math.Max(0, player.Health - damage);
        if (!player.IsDead)
        {
            player.ClearControllableState();
            player.AttackTimeRemaining = 0f;
            player.KnockbackVelocityX = knockbackVelocityX;
            player.HitStunTimeRemaining = hitStunDuration;
            player.InvulnerabilityTimeRemaining =
                GameProtocol.HitInvulnerabilityDuration;
            return;
        }

        player.DeathTimeSeconds = state.ServerTimeSeconds;
        player.ClearControllableState();
        player.AttackTimeRemaining = 0f;
        player.AttackCooldownRemaining = 0f;
        player.SkillCooldownRemaining = 0f;
        player.VerticalVelocity = 0f;
        player.KnockbackVelocityX = 0f;
        player.HitStunTimeRemaining = 0f;
        player.InvulnerabilityTimeRemaining = 0f;
        player.Z = 0f;
    }
}
