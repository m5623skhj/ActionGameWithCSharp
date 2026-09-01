using System.Threading.Channels;
using CSharpServer.Network;

namespace ActionGame.Server.Network;

internal sealed class ClientPeer : IDisposable
{
    private readonly long connectionId;
    private readonly IConnectionSender sender;
    private readonly ChannelWriter<ServerEvent> eventWriter;
    private readonly Channel<byte[]> outgoingPackets;
    private readonly CancellationTokenSource sendCancellation;
    private Task? sendTask;
    private int stopState;

    public ClientPeer(
        long connectionId,
        IConnectionSender sender,
        ChannelWriter<ServerEvent> eventWriter,
        CancellationToken serverCancellationToken)
    {
        this.connectionId = connectionId;
        this.sender = sender;
        this.eventWriter = eventWriter;
        sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            serverCancellationToken);
        outgoingPackets = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(8)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });
    }

    public Task Completion => sendTask ?? Task.CompletedTask;

    public void Start()
    {
        if (sendTask is not null)
        {
            throw new InvalidOperationException("The peer send loop is already running.");
        }

        sendTask = RunSendLoopAsync();
    }

    public bool TryQueue(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Volatile.Read(ref stopState) == 0
            && outgoingPackets.Writer.TryWrite(payload);
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref stopState, 1) != 0)
        {
            return;
        }

        outgoingPackets.Writer.TryComplete();
        sendCancellation.Cancel();
    }

    public void Dispose()
    {
        Stop();
        sendCancellation.Dispose();
    }

    private async Task RunSendLoopAsync()
    {
        try
        {
            await foreach (var payload in outgoingPackets.Reader.ReadAllAsync(
                sendCancellation.Token))
            {
                await sender.SendAsync(payload, sendCancellation.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (sendCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            eventWriter.TryWrite(
                new ConnectionLostServerEvent(connectionId, exception));
        }
    }
}
