using System.Net;
using System.Threading.Channels;
using ActionGame.Contracts.Protocol;
using ActionGame.Server.Network;
using CSharpServer.Network;

namespace ActionGame.Server;

public sealed class GameServerHost : IDisposable
{
    private readonly Channel<ServerEvent> serverEvents;
    private readonly TcpServer tcpServer;
    private long nextConnectionId;
    private int startState;
    private int runState;

    public GameServerHost(IPAddress address, int port)
    {
        ArgumentNullException.ThrowIfNull(address);
        serverEvents = Channel.CreateBounded<ServerEvent>(
            new BoundedChannelOptions(256)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
        tcpServer = new TcpServer(
            address,
            port,
            inBufferSize: 4096,
            maxConcurrentClients: 8,
            clientIdleTimeout: TimeSpan.FromSeconds(6),
            CreateConnection);
    }

    public int Port => tcpServer.Port;

    public void Start()
    {
        if (Interlocked.Exchange(ref startState, 1) != 0)
        {
            throw new InvalidOperationException("The game server is already started.");
        }

        tcpServer.Start();
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref startState) == 0)
        {
            throw new InvalidOperationException("Start must be called before RunAsync.");
        }

        if (Interlocked.Exchange(ref runState, 1) != 0)
        {
            throw new InvalidOperationException("The game server can only run once.");
        }

        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        var coordinator = new GameServerCoordinator(
            serverEvents.Writer,
            runCancellation.Token);
        var acceptTask = tcpServer.AcceptAndHandleConcurrently(runCancellation.Token);
        var tickTask = ProduceTicksAsync(runCancellation.Token);
        var eventTask = ProcessEventsAsync(coordinator, runCancellation.Token);

        try
        {
            await Task.WhenAny(acceptTask, tickTask, eventTask).ConfigureAwait(false);
            runCancellation.Cancel();
            await Task.WhenAll(acceptTask, tickTask, eventTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            tcpServer.Dispose();
            await coordinator.StopAsync().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        tcpServer.Dispose();
    }

    private StreamConnection CreateConnection(Stream stream, int bufferSize)
    {
        var connectionId = Interlocked.Increment(ref nextConnectionId);
        var handler = new GameConnectionPacketHandler(connectionId, serverEvents.Writer);
        return new StreamConnection(stream, bufferSize, handler);
    }

    private async Task ProduceTicksAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(1d / GameProtocol.SimulationRate));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await serverEvents.Writer.WriteAsync(
                new TickServerEvent(),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessEventsAsync(
        GameServerCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        await foreach (var serverEvent in serverEvents.Reader.ReadAllAsync(cancellationToken))
        {
            await coordinator.HandleAsync(serverEvent).ConfigureAwait(false);
        }
    }
}

