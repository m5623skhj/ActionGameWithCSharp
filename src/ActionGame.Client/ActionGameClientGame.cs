using System.Threading.Channels;
using ActionGame.Client.Network;
using ActionGame.Contracts.Protocol;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace ActionGame.Client;

public sealed class ActionGameClientGame : Game
{
    private readonly GraphicsDeviceManager graphics;
    private readonly Channel<ClientNetworkEvent> networkEvents;
    private readonly GameNetworkClient networkClient;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Dictionary<int, PlayerSnapshot> players = [];
    private SpriteBatch? spriteBatch;
    private Texture2D? pixel;
    private int? localPlayerId;
    private uint inputSequence;
    private double inputAccumulator;
    private long latestServerTick = -1;
    private bool networkStarted;
    private string connectionStatus = "Starting";

    public ActionGameClientGame(string host, int port)
    {
        graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = (int)GameProtocol.WorldWidth,
            PreferredBackBufferHeight = (int)GameProtocol.WorldHeight,
            SynchronizeWithVerticalRetrace = true,
        };
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1d / 60d);
        IsMouseVisible = true;
        Window.AllowUserResizing = false;

        networkEvents = Channel.CreateBounded<ClientNetworkEvent>(
            new BoundedChannelOptions(64)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
        networkClient = new GameNetworkClient(host, port, networkEvents.Writer);
        Window.Title = "Action Game - Starting";
    }

    protected override void Initialize()
    {
        networkClient.Start(shutdown.Token);
        networkStarted = true;
        connectionStatus = "Connecting";
        UpdateWindowTitle();
        base.Initialize();
    }

    protected override void LoadContent()
    {
        spriteBatch = new SpriteBatch(GraphicsDevice);
        pixel = new Texture2D(GraphicsDevice, 1, 1);
        pixel.SetData([Color.White]);
    }

    protected override void Update(GameTime gameTime)
    {
        var keyboard = Keyboard.GetState();
        if (keyboard.IsKeyDown(Keys.Escape))
        {
            Exit();
            return;
        }

        DrainNetworkEvents();
        QueueInput(keyboard, gameTime.ElapsedGameTime.TotalSeconds);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(24, 27, 33));
        if (spriteBatch is null || pixel is null)
        {
            return;
        }

        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        DrawWorldBorder(spriteBatch, pixel);
        foreach (var player in players.Values.OrderBy(player => player.PlayerId))
        {
            var color = player.PlayerId == localPlayerId
                ? new Color(80, 220, 120)
                : new Color(245, 150, 70);
            var rectangle = new Rectangle(
                (int)MathF.Round(player.X - (GameProtocol.PlayerSize / 2f)),
                (int)MathF.Round(player.Y - (GameProtocol.PlayerSize / 2f)),
                (int)GameProtocol.PlayerSize,
                (int)GameProtocol.PlayerSize);
            spriteBatch.Draw(pixel, rectangle, color);
        }

        spriteBatch.End();
        base.Draw(gameTime);
    }

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        if (networkStarted)
        {
            networkClient.StopAsync(sendLeave: localPlayerId.HasValue)
                .GetAwaiter()
                .GetResult();
        }

        shutdown.Cancel();
        base.OnExiting(sender, args);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            pixel?.Dispose();
            spriteBatch?.Dispose();
            shutdown.Dispose();
        }

        base.Dispose(disposing);
    }

    private void DrainNetworkEvents()
    {
        while (networkEvents.Reader.TryRead(out var networkEvent))
        {
            switch (networkEvent)
            {
                case ConnectedClientEvent:
                    connectionStatus = "Connected - joining";
                    break;
                case JoinAcceptedClientEvent accepted:
                    localPlayerId = accepted.Packet.PlayerId;
                    connectionStatus = $"Player {accepted.Packet.PlayerId}";
                    break;
                case JoinRejectedClientEvent rejected:
                    localPlayerId = null;
                    connectionStatus = $"Join rejected: {rejected.Packet.Reason}";
                    break;
                case WorldSnapshotClientEvent snapshot:
                    ApplySnapshot(snapshot.Packet);
                    break;
                case DisconnectedClientEvent disconnected:
                    localPlayerId = null;
                    players.Clear();
                    connectionStatus = $"Disconnected: {disconnected.Message}";
                    break;
            }
        }

        UpdateWindowTitle();
    }

    private void ApplySnapshot(WorldSnapshotPacket snapshot)
    {
        if (snapshot.ServerTick < latestServerTick)
        {
            return;
        }

        latestServerTick = snapshot.ServerTick;
        players.Clear();
        foreach (var player in snapshot.Players)
        {
            players.Add(player.PlayerId, player);
        }
    }

    private void QueueInput(KeyboardState keyboard, double elapsedSeconds)
    {
        if (!localPlayerId.HasValue)
        {
            inputAccumulator = 0d;
            return;
        }

        inputAccumulator += elapsedSeconds;
        var inputInterval = 1d / GameProtocol.SimulationRate;
        while (inputAccumulator >= inputInterval)
        {
            inputAccumulator -= inputInterval;
            var horizontal = GetAxis(
                keyboard.IsKeyDown(Keys.A),
                keyboard.IsKeyDown(Keys.D));
            var vertical = GetAxis(
                keyboard.IsKeyDown(Keys.W),
                keyboard.IsKeyDown(Keys.S));
            var payload = GamePacketCodec.EncodeInputCommand(
                new InputCommandPacket(++inputSequence, horizontal, vertical));
            if (!networkClient.TryQueue(payload))
            {
                connectionStatus = "Send queue unavailable";
                UpdateWindowTitle();
                break;
            }
        }
    }

    private void UpdateWindowTitle()
    {
        Window.Title = $"Action Game - {connectionStatus} - WASD / Esc";
    }

    private static sbyte GetAxis(bool negative, bool positive)
    {
        if (negative == positive)
        {
            return 0;
        }

        return negative ? (sbyte)-1 : (sbyte)1;
    }

    private static void DrawWorldBorder(SpriteBatch spriteBatch, Texture2D pixel)
    {
        var color = new Color(80, 86, 98);
        var width = (int)GameProtocol.WorldWidth;
        var height = (int)GameProtocol.WorldHeight;
        spriteBatch.Draw(pixel, new Rectangle(0, 0, width, 2), color);
        spriteBatch.Draw(pixel, new Rectangle(0, height - 2, width, 2), color);
        spriteBatch.Draw(pixel, new Rectangle(0, 0, 2, height), color);
        spriteBatch.Draw(pixel, new Rectangle(width - 2, 0, 2, height), color);
    }
}

