using System.Net;
using System.Net.Sockets;
using ActionGame.Contracts.Protocol;
using ActionGame.Server;

var port = GameProtocol.DefaultPort;
if (args.Length > 0 && !int.TryParse(args[0], out port))
{
    Console.Error.WriteLine("Usage: ActionGame.Server [port]");
    return 1;
}

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

using var server = new GameServerHost(IPAddress.Loopback, port);
try
{
    server.Start();
}
catch (SocketException exception)
    when (exception.SocketErrorCode == SocketError.AddressAlreadyInUse)
{
    Console.Error.WriteLine(
        $"Unable to start server: 127.0.0.1:{port} is already in use.");
    return 2;
}

Console.WriteLine($"Action game server listening on 127.0.0.1:{server.Port}.");
Console.WriteLine("Press Ctrl+C to stop.");
await server.RunAsync(shutdown.Token);
return 0;
