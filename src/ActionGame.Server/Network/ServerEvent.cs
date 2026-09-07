using ActionGame.Contracts.Protocol;
using CSharpServer.Network;

namespace ActionGame.Server.Network;

internal abstract record ServerEvent;

internal sealed record JoinServerEvent(
    long ConnectionId,
    IConnectionSender Sender) : ServerEvent;

internal sealed record MovementInputServerEvent(
    long ConnectionId,
    MovementInputPacket Input) : ServerEvent;

internal sealed record ActionCommandServerEvent(
    long ConnectionId,
    ActionCommandPacket Command) : ServerEvent;

internal sealed record LeaveServerEvent(long ConnectionId) : ServerEvent;

internal sealed record ConnectionLostServerEvent(
    long ConnectionId,
    Exception? Exception) : ServerEvent;

internal sealed record TickServerEvent : ServerEvent;
