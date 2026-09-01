using ActionGame.Contracts.Protocol;

namespace ActionGame.Server.Game;

public sealed class GameWorld
{
    private readonly Dictionary<long, PlayerState> playersByConnection = [];

    public int PlayerCount => playersByConnection.Count;

    public long ServerTick { get; private set; }

    public bool TryJoin(
        long connectionId,
        out int playerId,
        out JoinRejectReason rejectionReason)
    {
        if (playersByConnection.TryGetValue(connectionId, out var existingPlayer))
        {
            playerId = existingPlayer.PlayerId;
            rejectionReason = JoinRejectReason.AlreadyJoined;
            return false;
        }

        if (playersByConnection.Count >= GameProtocol.MaxPlayers)
        {
            playerId = 0;
            rejectionReason = JoinRejectReason.ServerFull;
            return false;
        }

        playerId = FindAvailablePlayerId();
        var spawnPosition = GetSpawnPosition(playerId);
        playersByConnection.Add(
            connectionId,
            new PlayerState(playerId, spawnPosition.X, spawnPosition.Y));
        rejectionReason = default;
        return true;
    }

    public bool Leave(long connectionId)
    {
        return playersByConnection.Remove(connectionId);
    }

    public bool ApplyInput(long connectionId, InputCommandPacket input)
    {
        if (input.Horizontal is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        if (input.Vertical is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        if (!playersByConnection.TryGetValue(connectionId, out var player))
        {
            return false;
        }

        if (player.HasInput && input.Sequence <= player.LastInputSequence)
        {
            return false;
        }

        player.HasInput = true;
        player.LastInputSequence = input.Sequence;
        player.Horizontal = input.Horizontal;
        player.Vertical = input.Vertical;
        return true;
    }

    public void Update(float deltaSeconds)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        var halfPlayerSize = GameProtocol.PlayerSize / 2f;
        foreach (var player in playersByConnection.Values)
        {
            var horizontal = (float)player.Horizontal;
            var vertical = (float)player.Vertical;
            var lengthSquared = (horizontal * horizontal) + (vertical * vertical);
            if (lengthSquared > 1f)
            {
                var inverseLength = 1f / MathF.Sqrt(lengthSquared);
                horizontal *= inverseLength;
                vertical *= inverseLength;
            }

            player.X = Math.Clamp(
                player.X + (horizontal * GameProtocol.PlayerSpeed * deltaSeconds),
                halfPlayerSize,
                GameProtocol.WorldWidth - halfPlayerSize);
            player.Y = Math.Clamp(
                player.Y + (vertical * GameProtocol.PlayerSpeed * deltaSeconds),
                halfPlayerSize,
                GameProtocol.WorldHeight - halfPlayerSize);
        }

        ServerTick++;
    }

    public WorldSnapshotPacket CreateSnapshot()
    {
        var players = playersByConnection.Values
            .OrderBy(player => player.PlayerId)
            .Select(player => new PlayerSnapshot(player.PlayerId, player.X, player.Y))
            .ToArray();
        return new WorldSnapshotPacket(ServerTick, players);
    }

    public bool TryGetPlayer(long connectionId, out PlayerSnapshot player)
    {
        if (playersByConnection.TryGetValue(connectionId, out var state))
        {
            player = new PlayerSnapshot(state.PlayerId, state.X, state.Y);
            return true;
        }

        player = default;
        return false;
    }

    private int FindAvailablePlayerId()
    {
        for (var playerId = 1; playerId <= GameProtocol.MaxPlayers; playerId++)
        {
            if (playersByConnection.Values.All(player => player.PlayerId != playerId))
            {
                return playerId;
            }
        }

        throw new InvalidOperationException("No player slot is available.");
    }

    private static (float X, float Y) GetSpawnPosition(int playerId)
    {
        return playerId switch
        {
            1 => (100f, GameProtocol.WorldHeight / 2f),
            2 => (GameProtocol.WorldWidth - 100f, GameProtocol.WorldHeight / 2f),
            _ => throw new ArgumentOutOfRangeException(nameof(playerId)),
        };
    }

    private sealed class PlayerState(int playerId, float x, float y)
    {
        public int PlayerId { get; } = playerId;

        public float X { get; set; } = x;

        public float Y { get; set; } = y;

        public sbyte Horizontal { get; set; }

        public sbyte Vertical { get; set; }

        public uint LastInputSequence { get; set; }

        public bool HasInput { get; set; }
    }
}
