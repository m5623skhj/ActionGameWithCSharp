using System.Threading.Channels;
using ActionGame.Contracts.Protocol;
using ActionGame.Server.Game;
using ActionGame.Server.Network;
using CSharpServer.Network;

namespace ActionGame.Server;

internal sealed class GameServerCoordinator(
    ChannelWriter<ServerEvent> eventWriter,
    CancellationToken serverCancellationToken)
{
    private const long IdleTickLimit = GameProtocol.SimulationRate * 5L;
    private readonly GameWorld world = new();
    private readonly Dictionary<long, PeerState> peers = [];
    private readonly List<ClientPeer> stoppedPeers = [];

    public async ValueTask HandleAsync(ServerEvent serverEvent)
    {
        switch (serverEvent)
        {
            case JoinServerEvent join:
                await HandleJoinAsync(join).ConfigureAwait(false);
                break;
            case MovementInputServerEvent input:
                HandleMovementInput(input);
                break;
            case ActionCommandServerEvent command:
                HandleActionCommand(command);
                break;
            case LeaveServerEvent leave:
                if (RemovePeer(leave.ConnectionId, out var leftRoomId))
                {
                    BroadcastSnapshot(leftRoomId);
                }

                break;
            case ConnectionLostServerEvent lost:
                if (lost.Exception is not null)
                {
                    Console.WriteLine(
                        $"Client {lost.ConnectionId} disconnected: {lost.Exception.Message}");
                }

                if (RemovePeer(lost.ConnectionId, out var lostRoomId))
                {
                    BroadcastSnapshot(lostRoomId);
                }

                break;
            case TickServerEvent:
                HandleTick();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(serverEvent));
        }
    }

    public async Task StopAsync()
    {
        foreach (var peerState in peers.Values)
        {
            peerState.Peer.Stop();
            stoppedPeers.Add(peerState.Peer);
        }

        peers.Clear();
        if (stoppedPeers.Count > 0)
        {
            await Task.WhenAll(stoppedPeers.Select(peer => peer.Completion))
                .ConfigureAwait(false);
            foreach (var peer in stoppedPeers)
            {
                peer.Dispose();
            }

            stoppedPeers.Clear();
        }
    }

    private async ValueTask HandleJoinAsync(JoinServerEvent join)
    {
        if (peers.TryGetValue(join.ConnectionId, out var existingPeer))
        {
            QueueOrRemove(
                join.ConnectionId,
                existingPeer.Peer,
                GamePacketCodec.EncodeJoinRejected(
                    new JoinRejectedPacket(JoinRejectReason.AlreadyJoined)));
            return;
        }

        const int roomId = GameWorld.DefaultRoomId;
        if (!world.TryJoinRoom(
            join.ConnectionId,
            roomId,
            out var playerId,
            out var joinFailure))
        {
            await SendRejectionAsync(
                join.Sender,
                MapJoinFailure(joinFailure)).ConfigureAwait(false);
            return;
        }

        if (!world.TryGetRoom(roomId, out var room))
        {
            throw new InvalidOperationException("The default room is missing.");
        }

        var peer = new ClientPeer(
            join.ConnectionId,
            join.Sender,
            eventWriter,
            serverCancellationToken);
        peer.Start();
        peers.Add(
            join.ConnectionId,
            new PeerState(peer, roomId, room.ServerTick));
        Console.WriteLine(
            $"Player {playerId} joined room {roomId} on connection {join.ConnectionId}.");

        QueueOrRemove(
            join.ConnectionId,
            peer,
            GamePacketCodec.EncodeJoinAccepted(new JoinAcceptedPacket(playerId)));
        BroadcastSnapshot(roomId);
    }

    private void HandleMovementInput(MovementInputServerEvent input)
    {
        if (!peers.TryGetValue(input.ConnectionId, out var peerState))
        {
            return;
        }

        if (!world.TryGetRoom(peerState.RoomId, out var room))
        {
            return;
        }

        peerState.LastInputTick = room.ServerTick;
        world.ApplyMovementInput(input.ConnectionId, input.Input);
    }

    private void HandleActionCommand(ActionCommandServerEvent command)
    {
        if (!peers.TryGetValue(command.ConnectionId, out var peerState))
        {
            return;
        }

        if (!world.TryGetRoom(peerState.RoomId, out var room))
        {
            return;
        }

        peerState.LastInputTick = room.ServerTick;
        world.ApplyActionCommand(command.ConnectionId, command.Command);
    }

    private void HandleTick()
    {
        world.Update(1f / GameProtocol.SimulationRate);

        var idleConnections = peers
            .Where(pair =>
                !world.TryGetRoom(pair.Value.RoomId, out var room)
                || room.ServerTick - pair.Value.LastInputTick > IdleTickLimit)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var connectionId in idleConnections)
        {
            RemovePeer(connectionId, out _);
        }

        BroadcastSnapshots();
    }

    private void BroadcastSnapshots()
    {
        foreach (var roomId in world.RoomIds)
        {
            BroadcastSnapshot(roomId);
        }
    }

    private void BroadcastSnapshot(int roomId)
    {
        if (!world.TryGetRoom(roomId, out var room))
        {
            return;
        }

        var payload = GamePacketCodec.EncodeWorldSnapshot(room.CreateSnapshot());
        var failedConnections = new List<long>();
        foreach (var pair in peers)
        {
            if (pair.Value.RoomId != roomId)
            {
                continue;
            }

            if (!pair.Value.Peer.TryQueue(payload))
            {
                failedConnections.Add(pair.Key);
            }
        }

        foreach (var connectionId in failedConnections)
        {
            RemovePeer(connectionId, out _);
        }
    }

    private void QueueOrRemove(long connectionId, ClientPeer peer, byte[] payload)
    {
        if (!peer.TryQueue(payload))
        {
            RemovePeer(connectionId, out _);
        }
    }

    private bool RemovePeer(long connectionId, out int roomId)
    {
        if (!peers.Remove(connectionId, out var peerState))
        {
            roomId = 0;
            return false;
        }

        roomId = peerState.RoomId;
        world.Leave(connectionId, out _, out var playerId);
        peerState.Peer.Stop();
        stoppedPeers.Add(peerState.Peer);
        Console.WriteLine(
            $"Player {playerId} left room {roomId} on connection {connectionId}.");
        return true;
    }

    private static JoinRejectReason MapJoinFailure(RoomJoinFailure failure)
    {
        return failure == RoomJoinFailure.AlreadyJoined
            ? JoinRejectReason.AlreadyJoined
            : JoinRejectReason.ServerFull;
    }

    private static async ValueTask SendRejectionAsync(
        IConnectionSender sender,
        JoinRejectReason reason)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await sender.SendAsync(
                GamePacketCodec.EncodeJoinRejected(new JoinRejectedPacket(reason)),
                timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException
            or ObjectDisposedException
            or OperationCanceledException)
        {
        }
    }

    private sealed class PeerState(
        ClientPeer peer,
        int roomId,
        long lastInputTick)
    {
        public ClientPeer Peer { get; } = peer;

        public int RoomId { get; } = roomId;

        public long LastInputTick { get; set; } = lastInputTick;
    }
}
