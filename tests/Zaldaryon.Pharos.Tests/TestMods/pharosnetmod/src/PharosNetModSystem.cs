using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace PharosNetMod;

[ProtoContract]
public class SyncPacket
{
    [ProtoMember(1)]
    public int Version { get; set; }

    [ProtoMember(2)]
    public string Text { get; set; } = "";
}

[ProtoContract]
public class SyncAck
{
    [ProtoMember(1)]
    public int Version { get; set; }
}

[ProtoContract]
public class Ping
{
    [ProtoMember(1)]
    public int N { get; set; }
}

[ProtoContract]
public class Unhandled
{
    [ProtoMember(1)]
    public int N { get; set; }
}

/// <summary>
/// A TCP channel, <c>pharosnet</c>, where each side acks the other's <see cref="SyncPacket"/>, and
/// a UDP channel, <c>pharosnetudp</c>, carrying <see cref="Ping"/>.
/// </summary>
public sealed class PharosNetModSystem : ModSystem
{
    public const string Channel = "pharosnet";
    public const string UdpChannel = "pharosnetudp";

    private string _lastText = "";
    private int _acks;
    private int _pings;

    public override void Start(ICoreAPI api)
    {
        api.Network.RegisterChannel(Channel)
            .RegisterMessageType<SyncPacket>()
            .RegisterMessageType<SyncAck>()
            .RegisterMessageType<Unhandled>();
        api.Network.RegisterUdpChannel(UdpChannel).RegisterMessageType<Ping>();
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        IServerNetworkChannel channel = api.Network.GetChannel(Channel)
            .SetMessageHandler<SyncPacket>((player, packet) => api.Network.GetChannel(Channel).SendPacket(new SyncAck { Version = packet.Version }, player))
            .SetMessageHandler<SyncAck>((player, ack) => { });
        api.Network.GetUdpChannel(UdpChannel).SetMessageHandler<Ping>((player, ping) => { });

        api.ChatCommands.Create("pharosnet").WithDescription("Pharos net test").RequiresPrivilege(Privilege.controlserver)
            .BeginSubCommand("sync")
                .WithArgs(api.ChatCommands.Parsers.Int("version"))
                .HandleWith(args =>
                {
                    channel.BroadcastPacket(new SyncPacket { Version = (int)args[0], Text = "from server" });
                    return TextCommandResult.Success();
                })
            .EndSubCommand();
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        IClientNetworkChannel channel = api.Network.GetChannel(Channel)
            .SetMessageHandler<SyncPacket>(packet =>
            {
                _lastText = packet.Text;
                api.Network.GetChannel(Channel).SendPacket(new SyncAck { Version = packet.Version });
            })
            .SetMessageHandler<SyncAck>(_ => _acks++);
        IClientNetworkChannel udp = api.Network.GetUdpChannel(UdpChannel).SetMessageHandler<Ping>(_ => _pings++);

        IChatCommand command = api.ChatCommands.Create("pharosnet").WithDescription("Pharos net test");
        command.BeginSubCommand("last")
            .HandleWith(_ => TextCommandResult.Success($"text={_lastText} acks={_acks} pings={_pings}"))
            .EndSubCommand();
        command.BeginSubCommand("send")
            .WithArgs(api.ChatCommands.Parsers.Int("version"))
            .HandleWith(args =>
            {
                channel.SendPacket(new SyncPacket { Version = (int)args[0], Text = "from client" });
                return TextCommandResult.Success();
            })
            .EndSubCommand();
        command.BeginSubCommand("ping")
            .WithArgs(api.ChatCommands.Parsers.Int("n"))
            .HandleWith(args =>
            {
                udp.SendPacket(new Ping { N = (int)args[0] });
                return TextCommandResult.Success();
            })
            .EndSubCommand();
    }
}
