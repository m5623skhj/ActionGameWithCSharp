using ActionGame.Contracts.Protocol;

namespace ActionGame.Tests.Protocol;

public sealed class GamePacketCodecTest
{
    [Fact]
    public void MovementInputRoundTrips()
    {
        var expected = new MovementInputPacket(42, -1, 1);

        var actual = GamePacketCodec.DecodeMovementInput(
            GamePacketCodec.EncodeMovementInput(expected));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ActionCommandRoundTrips()
    {
        var expected = new ActionCommandPacket(27, ActionId.WarriorDashSlash);

        var actual = GamePacketCodec.DecodeActionCommand(
            GamePacketCodec.EncodeActionCommand(expected));

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
                    false,
                    0,
                    true,
                    2.5f),
                new PlayerSnapshot(
                    2,
                    700f,
                    250f,
                    0f,
                    FacingDirection.Left,
                    false,
                    0,
                    true,
                    3,
                    false,
                    0f),
            ],
            [
                new ArrowSnapshot(
                    7,
                    1,
                    320f,
                    200f,
                    63f,
                    FacingDirection.Right,
                    true),
            ]);

        var actual = GamePacketCodec.DecodeWorldSnapshot(
            GamePacketCodec.EncodeWorldSnapshot(expected));

        Assert.Equal(expected.ServerTick, actual.ServerTick);
        Assert.Equal(expected.Players, actual.Players);
        Assert.Equal(expected.Arrows, actual.Arrows);
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
        var payload = GamePacketCodec.EncodeMovementInput(
            new MovementInputPacket(1, 0, 0));
        payload[^1] = 2;

        Assert.Throws<InvalidDataException>(() =>
            GamePacketCodec.DecodeMovementInput(payload));
    }

    [Fact]
    public void ActionCommandWithUnknownActionIdIsRejected()
    {
        var payload = GamePacketCodec.EncodeActionCommand(
            new ActionCommandPacket(1, ActionId.BasicAttack));
        payload[^2] = 0xff;
        payload[^1] = 0xff;

        Assert.Throws<InvalidDataException>(() =>
            GamePacketCodec.DecodeActionCommand(payload));
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
                    false,
                    0,
                    false,
                    0f),
                new PlayerSnapshot(
                    1,
                    20f,
                    160f,
                    0f,
                    FacingDirection.Left,
                    false,
                    GameProtocol.MaxHealth,
                    false,
                    0,
                    false,
                    0f),
            ],
            []);

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
                    false,
                    0,
                    false,
                    0f),
            ],
            []);

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
