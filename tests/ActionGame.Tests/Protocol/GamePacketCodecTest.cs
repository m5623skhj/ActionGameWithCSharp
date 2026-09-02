using ActionGame.Contracts.Protocol;

namespace ActionGame.Tests.Protocol;

public sealed class GamePacketCodecTest
{
    [Fact]
    public void InputCommandRoundTrips()
    {
        var expected = new InputCommandPacket(
            42,
            -1,
            1,
            InputActionFlags.Revive | InputActionFlags.ReservedZ);

        var actual = GamePacketCodec.DecodeInputCommand(
            GamePacketCodec.EncodeInputCommand(expected));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WorldSnapshotRoundTrips()
    {
        var expected = new WorldSnapshotPacket(
            17,
            [
                new PlayerSnapshot(
                    1,
                    100f,
                    200f,
                    35f,
                    FacingDirection.Right,
                    true,
                    80,
                    false),
                new PlayerSnapshot(
                    2,
                    700f,
                    250f,
                    0f,
                    FacingDirection.Left,
                    false,
                    0,
                    true),
            ]);

        var actual = GamePacketCodec.DecodeWorldSnapshot(
            GamePacketCodec.EncodeWorldSnapshot(expected));

        Assert.Equal(expected.ServerTick, actual.ServerTick);
        Assert.Equal(expected.Players, actual.Players);
    }

    [Fact]
    public void PacketWithUnsupportedVersionIsRejected()
    {
        var payload = GamePacketCodec.EncodeJoinRequest();
        payload[0]++;

        Assert.Throws<InvalidDataException>(() => GamePacketCodec.ReadPacketType(payload));
    }

    [Fact]
    public void InputWithOutOfRangeAxisIsRejected()
    {
        var payload = GamePacketCodec.EncodeInputCommand(
            new InputCommandPacket(1, 0, 0, InputActionFlags.None));
        payload[^2] = 2;

        Assert.Throws<InvalidDataException>(() =>
            GamePacketCodec.DecodeInputCommand(payload));
    }

    [Fact]
    public void InputWithUnknownActionFlagIsRejected()
    {
        var payload = GamePacketCodec.EncodeInputCommand(
            new InputCommandPacket(1, 0, 0, InputActionFlags.None));
        payload[^1] = 0b1000_0000;

        Assert.Throws<InvalidDataException>(() =>
            GamePacketCodec.DecodeInputCommand(payload));
    }

    [Fact]
    public void SnapshotWithDuplicatePlayerIdsIsRejected()
    {
        var packet = new WorldSnapshotPacket(
            1,
            [
                new PlayerSnapshot(
                    1,
                    10f,
                    150f,
                    0f,
                    FacingDirection.Right,
                    false,
                    GameProtocol.MaxHealth,
                    false),
                new PlayerSnapshot(
                    1,
                    20f,
                    160f,
                    0f,
                    FacingDirection.Left,
                    false,
                    GameProtocol.MaxHealth,
                    false),
            ]);

        Assert.Throws<ArgumentException>(() =>
            GamePacketCodec.EncodeWorldSnapshot(packet));
    }

    [Fact]
    public void SnapshotWithMismatchedDeadStateIsRejected()
    {
        var packet = new WorldSnapshotPacket(
            1,
            [
                new PlayerSnapshot(
                    1,
                    10f,
                    150f,
                    0f,
                    FacingDirection.Right,
                    false,
                    0,
                    false),
            ]);

        Assert.Throws<ArgumentException>(() =>
            GamePacketCodec.EncodeWorldSnapshot(packet));
    }

    [Fact]
    public void PacketWithUnexpectedTrailingBytesIsRejected()
    {
        var payload = new byte[]
        {
            GameProtocol.Version,
            (byte)PacketType.JoinRequest,
            0,
        };

        Assert.Throws<InvalidDataException>(() =>
            GamePacketCodec.DecodeJoinRequest(payload));
    }
}
