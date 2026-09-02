using System.Buffers.Binary;

namespace ActionGame.Contracts.Protocol;

public static class GamePacketCodec
{
    private const int HeaderSize = 2;
    private const int JoinAcceptedSize = HeaderSize + sizeof(int);
    private const int JoinRejectedSize = HeaderSize + sizeof(byte);
    private const int InputCommandSize = HeaderSize + sizeof(uint) + 3;
    private const int WorldSnapshotHeaderSize = HeaderSize + sizeof(long) + sizeof(byte);
    private const int PlayerSnapshotSize =
        sizeof(int) + (3 * sizeof(float)) + sizeof(byte) + sizeof(byte);
    private const InputActionFlags ValidInputActions =
        InputActionFlags.Attack | InputActionFlags.Jump | InputActionFlags.ReservedZ;

    public static PacketType ReadPacketType(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < HeaderSize)
        {
            throw new InvalidDataException("Game packet header is incomplete.");
        }

        if (payload[0] != GameProtocol.Version)
        {
            throw new InvalidDataException($"Unsupported game protocol version: {payload[0]}.");
        }

        var packetType = (PacketType)payload[1];
        if (!Enum.IsDefined(packetType))
        {
            throw new InvalidDataException($"Unknown game packet type: {payload[1]}.");
        }

        return packetType;
    }

    public static byte[] EncodeJoinRequest()
    {
        return CreateHeader(PacketType.JoinRequest, HeaderSize);
    }

    public static void DecodeJoinRequest(ReadOnlySpan<byte> payload)
    {
        ValidateExactPacket(payload, PacketType.JoinRequest, HeaderSize);
    }

    public static byte[] EncodeJoinAccepted(JoinAcceptedPacket packet)
    {
        ValidatePlayerId(packet.PlayerId);

        var payload = CreateHeader(PacketType.JoinAccepted, JoinAcceptedSize);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(HeaderSize), packet.PlayerId);
        return payload;
    }

    public static JoinAcceptedPacket DecodeJoinAccepted(ReadOnlySpan<byte> payload)
    {
        ValidateExactPacket(payload, PacketType.JoinAccepted, JoinAcceptedSize);
        var playerId = BinaryPrimitives.ReadInt32LittleEndian(payload[HeaderSize..]);
        if (playerId is < 1 or > GameProtocol.MaxPlayers)
        {
            throw new InvalidDataException($"Invalid player id: {playerId}.");
        }

        return new JoinAcceptedPacket(playerId);
    }

    public static byte[] EncodeJoinRejected(JoinRejectedPacket packet)
    {
        if (!Enum.IsDefined(packet.Reason))
        {
            throw new ArgumentOutOfRangeException(nameof(packet));
        }

        var payload = CreateHeader(PacketType.JoinRejected, JoinRejectedSize);
        payload[HeaderSize] = (byte)packet.Reason;
        return payload;
    }

    public static JoinRejectedPacket DecodeJoinRejected(ReadOnlySpan<byte> payload)
    {
        ValidateExactPacket(payload, PacketType.JoinRejected, JoinRejectedSize);
        var reason = (JoinRejectReason)payload[HeaderSize];
        if (!Enum.IsDefined(reason))
        {
            throw new InvalidDataException($"Unknown join rejection reason: {payload[HeaderSize]}.");
        }

        return new JoinRejectedPacket(reason);
    }

    public static byte[] EncodeInputCommand(InputCommandPacket packet)
    {
        ValidateInputAxis(packet.Horizontal, nameof(packet.Horizontal));
        ValidateInputAxis(packet.Depth, nameof(packet.Depth));
        ValidateInputActions(packet.Actions, nameof(packet.Actions));

        var payload = CreateHeader(PacketType.InputCommand, InputCommandSize);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(HeaderSize), packet.Sequence);
        payload[HeaderSize + sizeof(uint)] = unchecked((byte)packet.Horizontal);
        payload[HeaderSize + sizeof(uint) + 1] = unchecked((byte)packet.Depth);
        payload[HeaderSize + sizeof(uint) + 2] = (byte)packet.Actions;
        return payload;
    }

    public static InputCommandPacket DecodeInputCommand(ReadOnlySpan<byte> payload)
    {
        ValidateExactPacket(payload, PacketType.InputCommand, InputCommandSize);
        var sequence = BinaryPrimitives.ReadUInt32LittleEndian(payload[HeaderSize..]);
        var horizontal = unchecked((sbyte)payload[HeaderSize + sizeof(uint)]);
        var depth = unchecked((sbyte)payload[HeaderSize + sizeof(uint) + 1]);
        var actions = (InputActionFlags)payload[HeaderSize + sizeof(uint) + 2];
        ValidateDecodedInputAxis(horizontal, nameof(horizontal));
        ValidateDecodedInputAxis(depth, nameof(depth));
        ValidateDecodedInputActions(actions);
        return new InputCommandPacket(sequence, horizontal, depth, actions);
    }

    public static byte[] EncodeWorldSnapshot(WorldSnapshotPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(packet.Players);
        if (packet.ServerTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(packet));
        }

        if (packet.Players.Length > GameProtocol.MaxPlayers)
        {
            throw new ArgumentException("A snapshot contains too many players.", nameof(packet));
        }

        ValidateSnapshots(packet.Players, static message => new ArgumentException(message));

        var payload = CreateHeader(
            PacketType.WorldSnapshot,
            WorldSnapshotHeaderSize + (packet.Players.Length * PlayerSnapshotSize));
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(HeaderSize), packet.ServerTick);
        payload[HeaderSize + sizeof(long)] = (byte)packet.Players.Length;

        var offset = WorldSnapshotHeaderSize;
        foreach (var player in packet.Players)
        {
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset), player.PlayerId);
            BinaryPrimitives.WriteInt32LittleEndian(
                payload.AsSpan(offset + sizeof(int)),
                BitConverter.SingleToInt32Bits(player.X));
            BinaryPrimitives.WriteInt32LittleEndian(
                payload.AsSpan(offset + sizeof(int) + sizeof(float)),
                BitConverter.SingleToInt32Bits(player.Y));
            BinaryPrimitives.WriteInt32LittleEndian(
                payload.AsSpan(offset + sizeof(int) + (2 * sizeof(float))),
                BitConverter.SingleToInt32Bits(player.Z));
            payload[offset + sizeof(int) + (3 * sizeof(float))] =
                unchecked((byte)(sbyte)player.Facing);
            payload[offset + sizeof(int) + (3 * sizeof(float)) + 1] =
                player.IsAttacking ? (byte)1 : (byte)0;
            offset += PlayerSnapshotSize;
        }

        return payload;
    }

    public static WorldSnapshotPacket DecodeWorldSnapshot(ReadOnlySpan<byte> payload)
    {
        if (ReadPacketType(payload) != PacketType.WorldSnapshot)
        {
            throw new InvalidDataException("Expected a world snapshot packet.");
        }

        if (payload.Length < WorldSnapshotHeaderSize)
        {
            throw new InvalidDataException("World snapshot header is incomplete.");
        }

        var serverTick = BinaryPrimitives.ReadInt64LittleEndian(payload[HeaderSize..]);
        if (serverTick < 0)
        {
            throw new InvalidDataException("Server tick cannot be negative.");
        }

        var playerCount = payload[HeaderSize + sizeof(long)];
        if (playerCount > GameProtocol.MaxPlayers)
        {
            throw new InvalidDataException("World snapshot contains too many players.");
        }

        var expectedLength = WorldSnapshotHeaderSize + (playerCount * PlayerSnapshotSize);
        if (payload.Length != expectedLength)
        {
            throw new InvalidDataException("World snapshot length does not match its player count.");
        }

        var players = new PlayerSnapshot[playerCount];
        var offset = WorldSnapshotHeaderSize;
        for (var index = 0; index < playerCount; index++)
        {
            var playerId = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
            var x = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(payload[(offset + sizeof(int))..]));
            var y = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload[(offset + sizeof(int) + sizeof(float))..]));
            var z = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(
                    payload[(offset + sizeof(int) + (2 * sizeof(float)))..]));
            var facing = (FacingDirection)unchecked(
                (sbyte)payload[offset + sizeof(int) + (3 * sizeof(float))]);
            var attackingValue = payload[offset + sizeof(int) + (3 * sizeof(float)) + 1];
            if (attackingValue > 1)
            {
                throw new InvalidDataException("Invalid attacking state value.");
            }

            players[index] = new PlayerSnapshot(
                playerId,
                x,
                y,
                z,
                facing,
                attackingValue == 1);
            offset += PlayerSnapshotSize;
        }

        ValidateSnapshots(players, static message => new InvalidDataException(message));
        return new WorldSnapshotPacket(serverTick, players);
    }

    public static byte[] EncodeLeaveRequest()
    {
        return CreateHeader(PacketType.LeaveRequest, HeaderSize);
    }

    public static void DecodeLeaveRequest(ReadOnlySpan<byte> payload)
    {
        ValidateExactPacket(payload, PacketType.LeaveRequest, HeaderSize);
    }

    private static byte[] CreateHeader(PacketType packetType, int length)
    {
        var payload = new byte[length];
        payload[0] = GameProtocol.Version;
        payload[1] = (byte)packetType;
        return payload;
    }

    private static void ValidateExactPacket(
        ReadOnlySpan<byte> payload,
        PacketType expectedType,
        int expectedLength)
    {
        if (ReadPacketType(payload) != expectedType)
        {
            throw new InvalidDataException($"Expected a {expectedType} packet.");
        }

        if (payload.Length != expectedLength)
        {
            throw new InvalidDataException($"Invalid {expectedType} packet length.");
        }
    }

    private static void ValidateSnapshots(
        IReadOnlyList<PlayerSnapshot> players,
        Func<string, Exception> createException)
    {
        var playerIds = new HashSet<int>();
        foreach (var player in players)
        {
            if (player.PlayerId is < 1 or > GameProtocol.MaxPlayers)
            {
                throw createException($"Invalid player id: {player.PlayerId}.");
            }

            if (!playerIds.Add(player.PlayerId))
            {
                throw createException($"Duplicate player id: {player.PlayerId}.");
            }

            if (!float.IsFinite(player.X)
                || !float.IsFinite(player.Y)
                || !float.IsFinite(player.Z))
            {
                throw createException("Player coordinates must be finite.");
            }

            if (player.X < 0f
                || player.X > GameProtocol.WorldWidth
                || player.Y < GameProtocol.FloorTop
                || player.Y > GameProtocol.FloorBottom
                || player.Z < 0f
                || player.Z > GameProtocol.WorldHeight)
            {
                throw createException("Player coordinates are outside the world.");
            }

            if (!Enum.IsDefined(player.Facing))
            {
                throw createException($"Invalid facing direction: {player.Facing}.");
            }
        }
    }

    private static void ValidatePlayerId(int playerId)
    {
        if (playerId is < 1 or > GameProtocol.MaxPlayers)
        {
            throw new ArgumentOutOfRangeException(nameof(playerId));
        }
    }

    private static void ValidateInputAxis(sbyte axis, string parameterName)
    {
        if (axis is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateInputActions(
        InputActionFlags actions,
        string parameterName)
    {
        if ((actions & ~ValidInputActions) != 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateDecodedInputAxis(sbyte axis, string axisName)
    {
        if (axis is < -1 or > 1)
        {
            throw new InvalidDataException($"Invalid {axisName} input axis: {axis}.");
        }
    }

    private static void ValidateDecodedInputActions(InputActionFlags actions)
    {
        if ((actions & ~ValidInputActions) != 0)
        {
            throw new InvalidDataException($"Invalid input action flags: {actions}.");
        }
    }
}
