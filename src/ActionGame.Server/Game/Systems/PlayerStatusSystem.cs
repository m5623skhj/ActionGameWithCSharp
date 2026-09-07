namespace ActionGame.Server.Game;

internal static class PlayerStatusSystem
{
    public static void TickTimers(PlayerState player, float deltaSeconds)
    {
        player.AttackTimeRemaining = Math.Max(
            0f,
            player.AttackTimeRemaining - deltaSeconds);
        player.AttackCooldownRemaining = Math.Max(
            0f,
            player.AttackCooldownRemaining - deltaSeconds);
        player.HitStunTimeRemaining = Math.Max(
            0f,
            player.HitStunTimeRemaining - deltaSeconds);
        player.InvulnerabilityTimeRemaining = Math.Max(
            0f,
            player.InvulnerabilityTimeRemaining - deltaSeconds);
        player.SkillCooldownRemaining = Math.Max(
            0f,
            player.SkillCooldownRemaining - deltaSeconds);
    }
}
