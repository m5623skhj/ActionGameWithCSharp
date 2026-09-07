using System.Threading.Channels;
using ActionGame.Contracts.Protocol;
using CSharpServer.Network;

namespace ActionGame.Server.Network;

internal sealed class GameConnectionPacketHandler(
    long connectionId,
    ChannelWriter<ServerEvent> eventWriter) : IConnectionPacketHandler
{
    public void Handle(IConnectionSender sender, byte[] payload)
    {
        var serverEvent = DecodeClientPacket(sender, payload);
        if (!eventWriter.TryWrite(serverEvent))
        {
            throw new InvalidOperationException("The server event queue is full.");
        }
    }

    public async ValueTask HandleAsync(
        IConnectionSender sender,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var serverEvent = DecodeClientPacket(sender, payload);
        await eventWriter.WriteAsync(serverEvent, cancellationToken).ConfigureAwait(false);
    }

    private ServerEvent DecodeClientPacket(IConnectionSender sender, byte[] payload)
    {
        return GamePacketCodec.ReadPacketType(payload) switch
        {
            PacketType.JoinRequest => DecodeJoinRequest(sender, payload),
            PacketType.MovementInput => new MovementInputServerEvent(
                connectionId,
                GamePacketCodec.DecodeMovementInput(payload)),
            PacketType.ActionCommand => new ActionCommandServerEvent(
                connectionId,
                GamePacketCodec.DecodeActionCommand(payload)),
            PacketType.LeaveRequest => DecodeLeaveRequest(payload),
            var packetType => throw new InvalidDataException(
                $"Client sent disallowed packet type: {packetType}."),
        };
    }

    private JoinServerEvent DecodeJoinRequest(IConnectionSender sender, byte[] payload)
    {
        GamePacketCodec.DecodeJoinRequest(payload);
        return new JoinServerEvent(connectionId, sender);
    }

    private LeaveServerEvent DecodeLeaveRequest(byte[] payload)
    {
        GamePacketCodec.DecodeLeaveRequest(payload);
        return new LeaveServerEvent(connectionId);
    }
}
