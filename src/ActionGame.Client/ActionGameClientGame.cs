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
    private ArrowSnapshot[] arrows = [];
    private SpriteBatch? spriteBatch;
    private Texture2D? pixel;
    private LpcCharacterRenderer? characterRenderer;
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
        characterRenderer = LpcCharacterRenderer.Load(GraphicsDevice);
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
        characterRenderer?.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
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
            var shadowRectangle = new Rectangle(
                (int)MathF.Round(player.X - (GameProtocol.PlayerSize * 0.6f)),
                (int)MathF.Round(player.Y - 4f),
                (int)MathF.Round(GameProtocol.PlayerSize * 1.2f),
                8);
            spriteBatch.Draw(pixel, shadowRectangle, new Color(0, 0, 0, 110));

            if (player.PlayerId == localPlayerId)
            {
                var markerRectangle = new Rectangle(
                    (int)MathF.Round(player.X - 9f),
                    (int)MathF.Round(player.Y + 5f),
                    18,
                    3);
                spriteBatch.Draw(pixel, markerRectangle, new Color(80, 220, 120));
            }

            characterRenderer?.Draw(spriteBatch, player);
            DrawHealthBar(spriteBatch, pixel, player);
        }

        foreach (var arrow in arrows.OrderBy(arrow => arrow.Y))
        {
            DrawArrow(spriteBatch, pixel, arrow);
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
            characterRenderer?.Dispose();
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
                    arrows = [];
                    characterRenderer?.Reset();
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
        characterRenderer?.ApplySnapshot(snapshot.Players);
        players.Clear();
        foreach (var player in snapshot.Players)
        {
            players.Add(player.PlayerId, player);
        }

        arrows = snapshot.Arrows;
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
            var isDead = IsLocalPlayerDead();
            var horizontal = isDead
                ? (sbyte)0
                : GetAxis(
                    keyboard.IsKeyDown(Keys.Left),
                    keyboard.IsKeyDown(Keys.Right));
            var depth = isDead
                ? (sbyte)0
                : GetAxis(
                    keyboard.IsKeyDown(Keys.Up),
                    keyboard.IsKeyDown(Keys.Down));
            var allowedActions = isDead
                ? InputActionFlags.Revive | InputActionFlags.ReservedZ
                : InputActionFlags.Attack
                    | InputActionFlags.Jump
                    | InputActionFlags.ReservedZ;
            var actions = pendingActionPresses & allowedActions;
            if (keyboard.IsKeyDown(Keys.X))
            {
                actions |= isDead
                    ? InputActionFlags.Revive
                    : InputActionFlags.Attack;
            }

            if (!isDead && keyboard.IsKeyDown(Keys.C))
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
        var isDead = IsLocalPlayerDead();
        LatchPressedAction(
            keyboard,
            Keys.X,
            isDead ? InputActionFlags.Revive : InputActionFlags.Attack);
        if (!isDead)
        {
            LatchPressedAction(keyboard, Keys.C, InputActionFlags.Jump);
        }

        LatchPressedAction(keyboard, Keys.Z, InputActionFlags.ReservedZ);
    }

    private bool IsLocalPlayerDead()
    {
        return localPlayerId.HasValue
            && players.TryGetValue(localPlayerId.Value, out var player)
            && player.IsDead;
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
        Window.Title = $"Action Game - {connectionStatus} - Arrows / X Attack-Revive / C / Z / Esc";
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

    private static void DrawHealthBar(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        PlayerSnapshot player)
    {
        const int width = 48;
        const int height = 6;
        var screenY = player.Y - player.Z;
        var x = (int)MathF.Round(player.X - (width / 2f));
        var y = (int)MathF.Round(screenY - 58f);
        spriteBatch.Draw(pixel, new Rectangle(x, y, width, height), new Color(15, 17, 21));

        var fillWidth = (int)MathF.Round(
            (width - 2) * (player.Health / (float)GameProtocol.MaxHealth));
        if (fillWidth <= 0)
        {
            return;
        }

        var color = player.Health switch
        {
            > 50 => new Color(70, 210, 95),
            > 25 => new Color(235, 190, 55),
            _ => new Color(225, 70, 65),
        };
        spriteBatch.Draw(
            pixel,
            new Rectangle(x + 1, y + 1, fillWidth, height - 2),
            color);
    }

    private static void DrawArrow(
        SpriteBatch spriteBatch,
        Texture2D pixel,
        ArrowSnapshot arrow)
    {
        const int shaftLength = 16;
        var tipX = (int)MathF.Round(arrow.X);
        var y = (int)MathF.Round(arrow.Y - arrow.Z);
        var isFacingRight = arrow.Direction == FacingDirection.Right;
        var shaftX = isFacingRight ? tipX - shaftLength : tipX;
        var tailX = isFacingRight ? shaftX : shaftX + shaftLength - 2;
        var headX = isFacingRight ? tipX - 3 : tipX;

        spriteBatch.Draw(
            pixel,
            new Rectangle(shaftX, y, shaftLength, 2),
            new Color(135, 88, 45));
        spriteBatch.Draw(
            pixel,
            new Rectangle(headX, y - 2, 3, 6),
            new Color(205, 210, 220));
        spriteBatch.Draw(
            pixel,
            new Rectangle(tailX, y - 2, 2, 6),
            new Color(180, 65, 55));
    }
}
