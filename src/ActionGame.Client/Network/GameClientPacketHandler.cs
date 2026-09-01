using System.Threading.Channels;
using ActionGame.Contracts.Protocol;
using CSharpServer.Network;

namespace ActionGame.Client.Network;

internal sealed class GameClientPacketHandler(
    ChannelWriter<ClientNetworkEvent> eventWriter) : IConnectionPacketHandler
{
    public void Handle(IConnectionSender sender, byte[] payload)
    {
        var clientEvent = DecodeServerPacket(payload);
        if (!eventWriter.TryWrite(clientEvent))
        {
            throw new InvalidOperationException("The client event queue is full.");
        }
    }

    public async ValueTask HandleAsync(
        IConnectionSender sender,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var clientEvent = DecodeServerPacket(payload);
        await eventWriter.WriteAsync(clientEvent, cancellationToken).ConfigureAwait(false);
    }

    private static ClientNetworkEvent DecodeServerPacket(byte[] payload)
    {
        return GamePacketCodec.ReadPacketType(payload) switch
        {
            PacketType.JoinAccepted => new JoinAcceptedClientEvent(
                GamePacketCodec.DecodeJoinAccepted(payload)),
            PacketType.JoinRejected => new JoinRejectedClientEvent(
                GamePacketCodec.DecodeJoinRejected(payload)),
            PacketType.WorldSnapshot => new WorldSnapshotClientEvent(
                GamePacketCodec.DecodeWorldSnapshot(payload)),
            var packetType => throw new InvalidDataException(
                $"Server sent disallowed packet type: {packetType}."),
        };
    }
}

