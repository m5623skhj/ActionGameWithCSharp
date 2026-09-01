using System.Net.Sockets;
using System.Threading.Channels;
using ActionGame.Contracts.Protocol;
using CSharpServer.Network;

namespace ActionGame.Tests.Integration;

internal sealed class HeadlessGameClient : IAsyncDisposable
{
    private readonly TcpClient tcpClient;
    private readonly StreamConnection connection;
    private readonly CancellationTokenSource readCancellation = new();
    private readonly Channel<byte[]> receivedPackets = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });
    private readonly Task readTask;
    private int disposeState;

    private HeadlessGameClient(TcpClient tcpClient)
    {
        this.tcpClient = tcpClient;
        connection = new StreamConnection(
            tcpClient.GetStream(),
            inBufferSize: 4096,
            payload => receivedPackets.Writer.TryWrite(payload));
        readTask = connection.ReadUntilEndAsync(readCancellation.Token);
    }

    public static async Task<HeadlessGameClient> ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        var tcpClient = new TcpClient
        {
            NoDelay = true,
        };
        try
        {
            await tcpClient.ConnectAsync(host, port, cancellationToken);
            return new HeadlessGameClient(tcpClient);
        }
        catch
        {
            tcpClient.Dispose();
            throw;
        }
    }

    public ValueTask SendAsync(byte[] payload, CancellationToken cancellationToken)
    {
        return connection.SendAsync(payload, cancellationToken);
    }

    public async Task<JoinAcceptedPacket> JoinAsync(CancellationToken cancellationToken)
    {
        await SendAsync(GamePacketCodec.EncodeJoinRequest(), cancellationToken);
        while (true)
        {
            var payload = await receivedPackets.Reader.ReadAsync(cancellationToken);
            switch (GamePacketCodec.ReadPacketType(payload))
            {
                case PacketType.JoinAccepted:
                    return GamePacketCodec.DecodeJoinAccepted(payload);
                case PacketType.JoinRejected:
                    var rejection = GamePacketCodec.DecodeJoinRejected(payload);
                    throw new InvalidOperationException(
                        $"Join was rejected: {rejection.Reason}.");
            }
        }
    }

    public async Task<WorldSnapshotPacket> WaitForSnapshotAsync(
        Func<WorldSnapshotPacket, bool> predicate,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var payload = await receivedPackets.Reader.ReadAsync(cancellationToken);
            if (GamePacketCodec.ReadPacketType(payload) != PacketType.WorldSnapshot)
            {
                continue;
            }

            var snapshot = GamePacketCodec.DecodeWorldSnapshot(payload);
            if (predicate(snapshot))
            {
                return snapshot;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeState, 1) != 0)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            await connection.SendAsync(
                GamePacketCodec.EncodeLeaveRequest(),
                timeout.Token);
        }
        catch (Exception exception) when (exception is IOException
            or ObjectDisposedException
            or OperationCanceledException)
        {
        }

        readCancellation.Cancel();
        try
        {
            connection.Close();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }

        tcpClient.Dispose();
        try
        {
            await readTask;
        }
        catch (Exception exception) when (exception is IOException
            or ObjectDisposedException
            or OperationCanceledException)
        {
        }

        readCancellation.Dispose();
    }
}

