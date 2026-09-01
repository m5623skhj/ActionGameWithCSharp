using System.Net.Sockets;
using System.Threading.Channels;
using ActionGame.Contracts.Protocol;
using CSharpServer.Network;

namespace ActionGame.Client.Network;

internal sealed class GameNetworkClient(
    string host,
    int port,
    ChannelWriter<ClientNetworkEvent> eventWriter)
{
    private readonly Channel<byte[]> outgoingPackets = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(16)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    private CancellationTokenSource? runCancellation;
    private StreamConnection? connection;
    private Task? runTask;
    private int startState;
    private int stopState;

    public void Start(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref startState, 1) != 0)
        {
            throw new InvalidOperationException("The network client is already started.");
        }

        runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        runTask = RunAsync(runCancellation.Token);
    }

    public bool TryQueue(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Volatile.Read(ref stopState) == 0
            && outgoingPackets.Writer.TryWrite(payload);
    }

    public async Task StopAsync(bool sendLeave)
    {
        if (Interlocked.Exchange(ref stopState, 1) != 0)
        {
            if (runTask is not null)
            {
                await ObserveAsync(runTask).ConfigureAwait(false);
            }

            return;
        }

        var activeConnection = Volatile.Read(ref connection);
        if (sendLeave && activeConnection is not null)
        {
            using var leaveTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            try
            {
                await activeConnection.SendAsync(
                    GamePacketCodec.EncodeLeaveRequest(),
                    leaveTimeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException
                or ObjectDisposedException
                or OperationCanceledException)
            {
            }
        }

        outgoingPackets.Writer.TryComplete();
        runCancellation?.Cancel();
        try
        {
            activeConnection?.Close();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }

        if (runTask is not null)
        {
            await ObserveAsync(runTask).ConfigureAwait(false);
        }

        runCancellation?.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        Exception? disconnectException = null;
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
            client.NoDelay = true;
            var stream = client.GetStream();
            var packetHandler = new GameClientPacketHandler(eventWriter);
            var activeConnection = new StreamConnection(
                stream,
                inBufferSize: 4096,
                packetHandler);
            Volatile.Write(ref connection, activeConnection);
            await eventWriter.WriteAsync(
                new ConnectedClientEvent(),
                cancellationToken).ConfigureAwait(false);

            using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            var readTask = activeConnection.ReadUntilEndAsync(sessionCancellation.Token);
            var sendTask = RunSendLoopAsync(activeConnection, sessionCancellation.Token);
            if (!TryQueue(GamePacketCodec.EncodeJoinRequest()))
            {
                throw new InvalidOperationException("Unable to queue the join request.");
            }

            try
            {
                var completedTask = await Task.WhenAny(readTask, sendTask).ConfigureAwait(false);
                await completedTask.ConfigureAwait(false);
                if (!cancellationToken.IsCancellationRequested)
                {
                    disconnectException = new IOException("The server closed the connection.");
                }
            }
            finally
            {
                sessionCancellation.Cancel();
                try
                {
                    activeConnection.Close();
                }
                catch (Exception exception) when (exception is IOException
                    or ObjectDisposedException)
                {
                }

                await ObserveAsync(readTask).ConfigureAwait(false);
                await ObserveAsync(sendTask).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            disconnectException = exception;
        }
        finally
        {
            Volatile.Write(ref connection, null);
            if (!cancellationToken.IsCancellationRequested)
            {
                eventWriter.TryWrite(
                    new DisconnectedClientEvent(
                        disconnectException?.Message ?? "The server connection ended."));
            }
        }
    }

    private async Task RunSendLoopAsync(
        StreamConnection activeConnection,
        CancellationToken cancellationToken)
    {
        await foreach (var payload in outgoingPackets.Reader.ReadAllAsync(cancellationToken))
        {
            await activeConnection.SendAsync(payload, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
