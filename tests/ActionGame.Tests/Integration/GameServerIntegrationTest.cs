using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ActionGame.Contracts.Protocol;
using ActionGame.Server;

namespace ActionGame.Tests.Integration;

public sealed class GameServerIntegrationTest
{
    [Fact]
    public async Task OccupiedPortExitsCleanlyWithoutUnhandledException()
    {
        using var listener = new TcpListener(IPAddress.Loopback, port: 0)
        {
            ExclusiveAddressUse = true,
        };
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverPath = Path.Combine(AppContext.BaseDirectory, "ActionGame.Server.exe");
        var startInfo = new ProcessStartInfo(serverPath, port.ToString())
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the server process.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        var standardError = await process.StandardError.ReadToEndAsync();
        Assert.Equal(2, process.ExitCode);
        Assert.Contains("already in use", standardError);
        Assert.DoesNotContain("Unhandled exception", standardError);
    }

    [Fact]
    public async Task TwoClientsObserveAuthoritativeMovementAndLeave()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var serverCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            timeout.Token);
        using var server = new GameServerHost(IPAddress.Loopback, port: 0);
        server.Start();
        var serverTask = server.RunAsync(serverCancellation.Token);
        HeadlessGameClient? firstClient = null;
        HeadlessGameClient? secondClient = null;

        try
        {
            firstClient = await HeadlessGameClient.ConnectAsync(
                "127.0.0.1",
                server.Port,
                timeout.Token);
            var firstJoin = await firstClient.JoinAsync(timeout.Token);
            Assert.Equal(1, firstJoin.PlayerId);

            secondClient = await HeadlessGameClient.ConnectAsync(
                "127.0.0.1",
                server.Port,
                timeout.Token);
            var secondJoin = await secondClient.JoinAsync(timeout.Token);
            Assert.Equal(2, secondJoin.PlayerId);

            var initialSnapshot = await secondClient.WaitForSnapshotAsync(
                snapshot => snapshot.Players.Length == 2,
                timeout.Token);
            var initialFirstPlayer = Assert.Single(
                initialSnapshot.Players,
                player => player.PlayerId == firstJoin.PlayerId);

            await firstClient.SendAsync(
                GamePacketCodec.EncodeInputCommand(
                    new InputCommandPacket(1, 1, 0, InputActionFlags.None)),
                timeout.Token);
            var movedSnapshot = await firstClient.WaitForSnapshotAsync(
                snapshot => snapshot.Players.Any(
                    player => player.PlayerId == firstJoin.PlayerId
                        && player.X > initialFirstPlayer.X),
                timeout.Token);
            var movedFirstPlayer = Assert.Single(
                movedSnapshot.Players,
                player => player.PlayerId == firstJoin.PlayerId);

            var synchronizedSnapshot = await secondClient.WaitForSnapshotAsync(
                snapshot => snapshot.Players.Any(
                    player => player.PlayerId == firstJoin.PlayerId
                        && player.X >= movedFirstPlayer.X),
                timeout.Token);
            var synchronizedFirstPlayer = Assert.Single(
                synchronizedSnapshot.Players,
                player => player.PlayerId == firstJoin.PlayerId);
            Assert.True(synchronizedFirstPlayer.X >= movedFirstPlayer.X);

            await firstClient.SendAsync(
                GamePacketCodec.EncodeInputCommand(
                    new InputCommandPacket(
                        2,
                        0,
                        0,
                        InputActionFlags.Jump | InputActionFlags.Attack)),
                timeout.Token);
            var actionSnapshot = await firstClient.WaitForSnapshotAsync(
                snapshot => snapshot.Players.Any(
                    player => player.PlayerId == firstJoin.PlayerId
                        && player.Z > 0f
                        && player.IsAttacking),
                timeout.Token);
            var actionPlayer = Assert.Single(
                actionSnapshot.Players,
                player => player.PlayerId == firstJoin.PlayerId);

            var synchronizedActionSnapshot = await secondClient.WaitForSnapshotAsync(
                snapshot => snapshot.Players.Any(
                    player => player.PlayerId == firstJoin.PlayerId
                        && player.Z >= actionPlayer.Z
                        && player.IsAttacking),
                timeout.Token);
            Assert.Contains(
                synchronizedActionSnapshot.Players,
                player => player.PlayerId == firstJoin.PlayerId
                    && player.Z > 0f
                    && player.IsAttacking);

            await firstClient.SendAsync(
                GamePacketCodec.EncodeInputCommand(
                    new InputCommandPacket(3, 0, 0, InputActionFlags.None)),
                timeout.Token);
            await secondClient.DisposeAsync();
            secondClient = null;

            var leaveSnapshot = await firstClient.WaitForSnapshotAsync(
                snapshot => snapshot.Players.Length == 1,
                timeout.Token);
            Assert.Equal(firstJoin.PlayerId, leaveSnapshot.Players[0].PlayerId);
        }
        finally
        {
            if (secondClient is not null)
            {
                await secondClient.DisposeAsync();
            }

            if (firstClient is not null)
            {
                await firstClient.DisposeAsync();
            }

            serverCancellation.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
