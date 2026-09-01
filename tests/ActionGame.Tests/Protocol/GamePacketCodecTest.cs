using ActionGame.Contracts.Protocol;

namespace ActionGame.Tests.Protocol;

public sealed class GamePacketCodecTest
{
    [Fact]
    public void InputCommandRoundTrips()
    {
        var expected = new InputCommandPacket(42, -1, 1);

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
                new PlayerSnapshot(1, 100f, 200f),
                new PlayerSnapshot(2, 700f, 250f),
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
            new InputCommandPacket(1, 0, 0));
        payload[^1] = 2;

        Assert.Throws<InvalidDataException>(() =>
            GamePacketCodec.DecodeInputCommand(payload));
    }

    [Fact]
    public void SnapshotWithDuplicatePlayerIdsIsRejected()
    {
        var packet = new WorldSnapshotPacket(
            1,
            [
                new PlayerSnapshot(1, 10f, 10f),
                new PlayerSnapshot(1, 20f, 20f),
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
