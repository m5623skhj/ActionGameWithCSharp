using ActionGame.Contracts.Protocol;

namespace ActionGame.Client.Network;

internal abstract record ClientNetworkEvent;

internal sealed record ConnectedClientEvent : ClientNetworkEvent;

internal sealed record JoinAcceptedClientEvent(
    JoinAcceptedPacket Packet) : ClientNetworkEvent;

internal sealed record JoinRejectedClientEvent(
    JoinRejectedPacket Packet) : ClientNetworkEvent;

internal sealed record WorldSnapshotClientEvent(
    WorldSnapshotPacket Packet) : ClientNetworkEvent;

internal sealed record DisconnectedClientEvent(
    string Message) : ClientNetworkEvent;

