using System.Net;
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
server.Start();
Console.WriteLine($"Action game server listening on 127.0.0.1:{server.Port}.");
Console.WriteLine("Press Ctrl+C to stop.");
await server.RunAsync(shutdown.Token);
return 0;
