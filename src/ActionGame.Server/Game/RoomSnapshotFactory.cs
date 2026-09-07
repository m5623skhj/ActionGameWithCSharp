using ActionGame.Contracts.Protocol;

namespace ActionGame.Server.Game;

internal sealed class RoomSnapshotFactory(
    RoomState state,
    RespawnSystem respawnSystem)
{
    public WorldSnapshotPacket CreateSnapshot()
    {
        var players = state.PlayersByConnection.Values
            .OrderBy(player => player.PlayerId)
            .Select(CreatePlayerSnapshot)
            .ToArray();
        var arrows = state.Arrows
            .OrderBy(arrow => arrow.ArrowId)
            .Select(arrow => new ArrowSnapshot(
                arrow.ArrowId,
                arrow.OwnerPlayerId,
                arrow.X,
                arrow.Y,
                arrow.Z,
                arrow.Direction,
                arrow.IsSkillArrow))
            .ToArray();
        return new WorldSnapshotPacket(state.ServerTick, players, arrows);
    }

    public bool TryGetPlayer(long connectionId, out PlayerSnapshot player)
    {
        if (state.PlayersByConnection.TryGetValue(connectionId, out var statePlayer))
        {
            player = CreatePlayerSnapshot(statePlayer);
            return true;
        }

        player = default;
        return false;
    }

    private PlayerSnapshot CreatePlayerSnapshot(PlayerState player)
    {
        return new PlayerSnapshot(
            player.PlayerId,
            player.X,
            player.Y,
            player.Z,
            player.Facing,
            player.AttackTimeRemaining > 0f,
            player.Health,
            player.IsDead,
            respawnSystem.GetReviveSecondsRemaining(player),
            player.InvulnerabilityTimeRemaining > 0f,
            player.SkillCooldownRemaining);
    }
}
