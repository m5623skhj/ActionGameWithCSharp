using ActionGame.Client;
using ActionGame.Contracts.Protocol;

var host = args.Length > 0 ? args[0] : "127.0.0.1";
var port = GameProtocol.DefaultPort;
if (args.Length > 1 && !int.TryParse(args[1], out port))
{
    return 1;
}

using var game = new ActionGameClientGame(host, port);
game.Run();
return 0;
