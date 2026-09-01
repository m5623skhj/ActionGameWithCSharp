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
            case InputServerEvent input:
                HandleInput(input);
                break;
            case LeaveServerEvent leave:
                RemovePeer(leave.ConnectionId);
                BroadcastSnapshot();
                break;
            case ConnectionLostServerEvent lost:
                if (lost.Exception is not null)
                {
                    Console.WriteLine(
                        $"Client {lost.ConnectionId} disconnected: {lost.Exception.Message}");
                }

                RemovePeer(lost.ConnectionId);
                BroadcastSnapshot();
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

        if (!world.TryJoin(
            join.ConnectionId,
            out var playerId,
            out var rejectionReason))
        {
            await SendRejectionAsync(join.Sender, rejectionReason).ConfigureAwait(false);
            return;
        }

        var peer = new ClientPeer(
            join.ConnectionId,
            join.Sender,
            eventWriter,
            serverCancellationToken);
        peer.Start();
        peers.Add(join.ConnectionId, new PeerState(peer, world.ServerTick));
        Console.WriteLine($"Player {playerId} joined on connection {join.ConnectionId}.");

        QueueOrRemove(
            join.ConnectionId,
            peer,
            GamePacketCodec.EncodeJoinAccepted(new JoinAcceptedPacket(playerId)));
        BroadcastSnapshot();
    }

    private void HandleInput(InputServerEvent input)
    {
        if (!peers.TryGetValue(input.ConnectionId, out var peerState))
        {
            return;
        }

        peerState.LastInputTick = world.ServerTick;
        world.ApplyInput(input.ConnectionId, input.Input);
    }

    private void HandleTick()
    {
        world.Update(1f / GameProtocol.SimulationRate);

        var idleConnections = peers
            .Where(pair => world.ServerTick - pair.Value.LastInputTick > IdleTickLimit)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var connectionId in idleConnections)
        {
            RemovePeer(connectionId);
        }

        BroadcastSnapshot();
    }

    private void BroadcastSnapshot()
    {
        if (peers.Count == 0)
        {
            return;
        }

        var payload = GamePacketCodec.EncodeWorldSnapshot(world.CreateSnapshot());
        var failedConnections = new List<long>();
        foreach (var pair in peers)
        {
            if (!pair.Value.Peer.TryQueue(payload))
            {
                failedConnections.Add(pair.Key);
            }
        }

        foreach (var connectionId in failedConnections)
        {
            RemovePeer(connectionId);
        }
    }

    private void QueueOrRemove(long connectionId, ClientPeer peer, byte[] payload)
    {
        if (!peer.TryQueue(payload))
        {
            RemovePeer(connectionId);
        }
    }

    private void RemovePeer(long connectionId)
    {
        if (!peers.Remove(connectionId, out var peerState))
        {
            return;
        }

        var playerId = world.TryGetPlayer(connectionId, out var player)
            ? player.PlayerId
            : 0;
        world.Leave(connectionId);
        peerState.Peer.Stop();
        stoppedPeers.Add(peerState.Peer);
        Console.WriteLine($"Player {playerId} left connection {connectionId}.");
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

    private sealed class PeerState(ClientPeer peer, long lastInputTick)
    {
        public ClientPeer Peer { get; } = peer;

        public long LastInputTick { get; set; } = lastInputTick;
    }
}
