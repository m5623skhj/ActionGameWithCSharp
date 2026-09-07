using ActionGame.Contracts.Protocol;

namespace ActionGame.Server.Game;

internal sealed class RespawnSystem(RoomState state)
{
    public void UpdateDeadPlayer(PlayerState player, bool reviveRequested)
    {
        if (reviveRequested && CanRevive(player))
        {
            Revive(player);
            return;
        }

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

    public byte GetReviveSecondsRemaining(PlayerState player)
    {
        if (!player.IsDead || !player.DeathTimeSeconds.HasValue)
        {
            return 0;
        }

        var remainingSeconds = GameProtocol.ReviveDelaySeconds
            - (state.ServerTimeSeconds - player.DeathTimeSeconds.Value);
        return (byte)Math.Clamp(
            (int)Math.Ceiling(remainingSeconds),
            0,
            GameProtocol.ReviveDelaySeconds);
    }

    private bool CanRevive(PlayerState player)
    {
        return player.DeathTimeSeconds.HasValue
            && state.ServerTimeSeconds - player.DeathTimeSeconds.Value
                >= GameProtocol.ReviveDelaySeconds;
    }

    private static void Revive(PlayerState player)
    {
        player.Health = GameProtocol.MaxHealth;
        player.DeathTimeSeconds = null;
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
