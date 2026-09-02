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
    private KeyboardState previousKeyboard;
    private InputActionFlags pendingActionPresses;
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
        CaptureActionPresses(keyboard);
        QueueInput(keyboard, gameTime.ElapsedGameTime.TotalSeconds);
        previousKeyboard = keyboard;
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
        foreach (var player in players.Values
            .OrderBy(player => player.Y)
            .ThenBy(player => player.PlayerId))
        {
            var color = player.PlayerId == localPlayerId
                ? new Color(80, 220, 120)
                : new Color(245, 150, 70);
            var shadowRectangle = new Rectangle(
                (int)MathF.Round(player.X - (GameProtocol.PlayerSize * 0.6f)),
                (int)MathF.Round(player.Y - 4f),
                (int)MathF.Round(GameProtocol.PlayerSize * 1.2f),
                8);
            spriteBatch.Draw(pixel, shadowRectangle, new Color(0, 0, 0, 110));

            var screenY = player.Y - player.Z;
            if (player.IsAttacking)
            {
                var attackWidth = (int)MathF.Round(GameProtocol.AttackReach);
                var attackX = player.Facing == FacingDirection.Right
                    ? (int)MathF.Round(player.X + (GameProtocol.PlayerSize / 2f))
                    : (int)MathF.Round(
                        player.X - (GameProtocol.PlayerSize / 2f) - attackWidth);
                var attackRectangle = new Rectangle(
                    attackX,
                    (int)MathF.Round(screenY - 8f),
                    attackWidth,
                    16);
                spriteBatch.Draw(pixel, attackRectangle, new Color(255, 220, 80, 170));
            }

            var rectangle = new Rectangle(
                (int)MathF.Round(player.X - (GameProtocol.PlayerSize / 2f)),
                (int)MathF.Round(screenY - (GameProtocol.PlayerSize / 2f)),
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
            pendingActionPresses = InputActionFlags.None;
            return;
        }

        inputAccumulator += elapsedSeconds;
        var inputInterval = 1d / GameProtocol.SimulationRate;
        while (inputAccumulator >= inputInterval)
        {
            inputAccumulator -= inputInterval;
            var horizontal = GetAxis(
                keyboard.IsKeyDown(Keys.Left),
                keyboard.IsKeyDown(Keys.Right));
            var depth = GetAxis(
                keyboard.IsKeyDown(Keys.Up),
                keyboard.IsKeyDown(Keys.Down));
            var actions = pendingActionPresses;
            if (keyboard.IsKeyDown(Keys.X))
            {
                actions |= InputActionFlags.Attack;
            }

            if (keyboard.IsKeyDown(Keys.C))
            {
                actions |= InputActionFlags.Jump;
            }

            if (keyboard.IsKeyDown(Keys.Z))
            {
                actions |= InputActionFlags.ReservedZ;
            }

            var payload = GamePacketCodec.EncodeInputCommand(
                new InputCommandPacket(++inputSequence, horizontal, depth, actions));
            if (!networkClient.TryQueue(payload))
            {
                connectionStatus = "Send queue unavailable";
                UpdateWindowTitle();
                break;
            }

            pendingActionPresses = InputActionFlags.None;
        }
    }

    private void CaptureActionPresses(KeyboardState keyboard)
    {
        LatchPressedAction(keyboard, Keys.X, InputActionFlags.Attack);
        LatchPressedAction(keyboard, Keys.C, InputActionFlags.Jump);
        LatchPressedAction(keyboard, Keys.Z, InputActionFlags.ReservedZ);
    }

    private void LatchPressedAction(
        KeyboardState keyboard,
        Keys key,
        InputActionFlags action)
    {
        if (keyboard.IsKeyDown(key) && previousKeyboard.IsKeyUp(key))
        {
            pendingActionPresses |= action;
        }
    }

    private void UpdateWindowTitle()
    {
        Window.Title = $"Action Game - {connectionStatus} - Arrows / X / C / Z / Esc";
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
        spriteBatch.Draw(
            pixel,
            new Rectangle(0, (int)GameProtocol.FloorTop, width, 2),
            new Color(55, 62, 73));
        spriteBatch.Draw(
            pixel,
            new Rectangle(0, (int)GameProtocol.FloorBottom, width, 2),
            new Color(55, 62, 73));
    }
}
