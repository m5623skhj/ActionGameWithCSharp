using ActionGame.Contracts.Protocol;

namespace ActionGame.Server.Game;

internal sealed class PlayerRoster(RoomState state)
{
    public int PlayerCount => state.PlayersByConnection.Count;

    public bool TryJoin(
        long connectionId,
        out int playerId,
        out JoinRejectReason rejectionReason)
    {
        if (state.PlayersByConnection.TryGetValue(
            connectionId,
            out var existingPlayer))
        {
            playerId = existingPlayer.PlayerId;
            rejectionReason = JoinRejectReason.AlreadyJoined;
            return false;
        }

        if (state.PlayersByConnection.Count >= GameProtocol.MaxPlayers)
        {
            playerId = 0;
            rejectionReason = JoinRejectReason.ServerFull;
            return false;
        }

        playerId = FindAvailablePlayerId();
        var spawnPosition = GetSpawnPosition(playerId);
        state.PlayersByConnection.Add(
            connectionId,
            new PlayerState(playerId, spawnPosition.X, spawnPosition.Y));
        rejectionReason = default;
        return true;
    }

    public bool TryLeave(long connectionId, out PlayerState player)
    {
        return state.PlayersByConnection.Remove(connectionId, out player!);
    }

    private int FindAvailablePlayerId()
    {
        for (var playerId = 1; playerId <= GameProtocol.MaxPlayers; playerId++)
        {
            if (state.PlayersByConnection.Values.All(
                player => player.PlayerId != playerId))
            {
                return playerId;
            }
        }

        throw new InvalidOperationException("No player slot is available.");
    }

    private static (float X, float Y) GetSpawnPosition(int playerId)
    {
        var middleDepth = (GameProtocol.FloorTop + GameProtocol.FloorBottom) / 2f;
        return playerId switch
        {
            1 => (100f, middleDepth),
            2 => (GameProtocol.WorldWidth - 100f, middleDepth),
            _ => throw new ArgumentOutOfRangeException(nameof(playerId)),
        };
    }
}
