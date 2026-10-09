using ProtoBuf;
using Vintagestory.API.Common;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Network;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Network;

// Look-alikes of pharosnetmod's messages: the game compiles the mod, so the test cannot see its types.
#pragma warning disable SA1402
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

/// <summary>A mod's network messages, seen and injected on a real engine-mode client joined to a server.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerMods("TestMods/pharosnetmod")]
public class LiveModNetworkTests : ClientServerScenarioBase
{
    private const string Channel = "pharosnet";
    private const string Udp = "pharosnetudp";

    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario]
    public async Task ADeliveredMessage_ReachesTheHandler_AndItsReplyIsSeen()
    {
        ModMessage delivered = await Client!.ModNetwork.DeliverAsync(Channel, new SyncPacket { Version = 1, Text = "injected" });

        Assert.True(delivered.Injected);
        Assert.Equal(ModMessageDirection.Received, delivered.Direction);
        Assert.Equal("PharosNetMod.SyncPacket", delivered.TypeName);
        SyncAck ack = await Client.ModNetwork.WaitForSentAsync<SyncAck>(Channel, a => a.Version == 1, since: delivered.Sequence);
        Assert.Equal(1, ack.Version);
        Assert.Contains(Client.ModNetwork.Received<SyncPacket>(Channel), p => p.Text == "injected");
        Assert.StartsWith("text=injected", (await Client.Commands.ExecuteSuccessAsync(".pharosnet last")).Message);
        Assert.Single(Client.ModNetwork.Messages(Channel), m => m.Injected);
    }

    [ClientServerScenario]
    public async Task MessagesFromAndToTheServer_AreRecordedInOrder()
    {
        long mark = Client!.ModNetwork.Mark();
        ServerHost!.RunOnGameThread(() => ((Vintagestory.API.Server.ICoreServerAPI)Server!.Api).ChatCommands.ExecuteUnparsed(
            "/pharosnet sync 7", new TextCommandCallingArgs { Caller = ServerScenarioBase.ConsoleCaller() }, _ => { }));

        SyncPacket fromServer = await Client.ModNetwork.WaitForReceivedAsync<SyncPacket>(Channel, p => p.Version == 7, since: mark);
        Assert.Equal("from server", fromServer.Text);
        SyncAck reply = await Client.ModNetwork.WaitForSentAsync<SyncAck>(Channel, a => a.Version == 7, since: mark);
        Assert.Equal(7, reply.Version);

        ModMessage received = Assert.Single(Client.ModNetwork.Of<SyncPacket>(Channel, ModMessageDirection.Received), m => m.Sequence > mark);
        ModMessage sent = Assert.Single(Client.ModNetwork.Of<SyncAck>(Channel, ModMessageDirection.Sent), m => m.Sequence > mark);
        Assert.False(received.Injected);
        Assert.True(received.Sequence < sent.Sequence);
        Assert.True(received.Frame > 0 && sent.Frame >= received.Frame);

        // The client's own message is answered by the server.
        long beforeSend = Client.ModNetwork.Mark();
        await Client.Commands.ExecuteSuccessAsync(".pharosnet send 9");
        Assert.Equal(9, (await Client.ModNetwork.WaitForReceivedAsync<SyncAck>(Channel, a => a.Version == 9, since: beforeSend)).Version);
        Assert.Contains(Client.ModNetwork.Sent<SyncPacket>(Channel), p => p.Version == 9 && p.Text == "from client");
    }

    [ClientServerScenario]
    public async Task UdpMessages_AreRecordedAndDelivered()
    {
        long mark = Client!.ModNetwork.Mark();
        await Client.Commands.ExecuteSuccessAsync(".pharosnet ping 3");

        Ping sent = await Client.ModNetwork.WaitForSentAsync<Ping>(Udp, since: mark);
        Assert.Equal(3, sent.N);
        Assert.True(Assert.Single(Client.ModNetwork.Of<Ping>(Udp, ModMessageDirection.Sent), m => m.Sequence > mark).Udp);

        await Client.ModNetwork.DeliverAsync(Udp, new Ping { N = 5 });
        Assert.EndsWith("pings=1", (await Client.Commands.ExecuteSuccessAsync(".pharosnet last")).Message);

        ModChannelInfo udp = Assert.Single(Client.ModNetwork.Channels(), c => c.Name == Udp);
        Assert.True(udp.Udp && udp.Connected);
        Assert.Contains("PharosNetMod.Ping", udp.MessageTypes.Values);
    }

    [ClientServerScenario]
    public async Task Mistakes_AreReportedClearly()
    {
        ArgumentException noChannel = await Assert.ThrowsAsync<ArgumentException>(() => Client!.ModNetwork.DeliverAsync("nope", new Ping()));
        Assert.Contains(Channel, noChannel.Message);
        ArgumentException noType = Assert.Throws<ArgumentException>(() => Client!.ModNetwork.Sent<string>(Channel));
        Assert.Contains("PharosNetMod.SyncAck", noType.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Client!.ModNetwork.DeliverAsync(Channel, new Unhandled { N = 1 }));
        await Assert.ThrowsAsync<TimeoutException>(() => Client!.ModNetwork.WaitForSentAsync<SyncAck>(Channel, maxFrames: 5));
    }
}
