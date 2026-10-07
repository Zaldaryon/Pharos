using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Network;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Network;

[Collection("Sequential")]
public class LoopbackLinkTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario]
    public async Task PacketRecorder_CapturesBothDirectionsOfTheRealTraffic()
    {
        Client!.PacketRecorder.Start();
        await Session!.HoldAsync(PlayerAction.Forward, 30);
        Client.PacketRecorder.Stop();

        IReadOnlyList<RecordedPacket> packets = Client.PacketRecorder.GetRecordedPackets();
        Assert.Contains(packets, p => p.Direction == PacketDirection.Outbound && p.PacketId >= 0);
        Assert.Contains(packets, p => p.Direction == PacketDirection.Inbound && p.PacketId >= 0);
    }

    [ClientServerScenario]
    public async Task Latency_DelaysWhatTheServerSends()
    {
        await Session!.StepFramesAsync(30);
        int baseline = await FramesUntilBlockSeen(Ground(0));

        Client!.NetworkDegradation.Configure(new DegradedNetworkProfile(LatencyMs: 500));
        int delayed = await FramesUntilBlockSeen(Ground(2));

        Assert.True(baseline <= 10, $"Without latency the change took {baseline} frames");
        Assert.True(delayed >= 25, $"With 500 ms of latency the change took only {delayed} frames");
    }

    [ClientServerScenario]
    public async Task UdpLoss_DropsDatagramsWithoutBreakingTheConnection()
    {
        await Session!.StepFramesAsync(30);
        Client!.NetworkDegradation.Configure(new DegradedNetworkProfile(PacketDropRate: 1f));

        await Session.HoldAsync(PlayerAction.Forward, 60);

        Assert.True(Client.NetworkDegradation.PacketsDropped > 0, "No datagram went through the link");
        Assert.True(Session.IsConnected);
        Assert.Null(Client.Client.disconnectReason);
    }

    [ClientServerScenario]
    public async Task SimulateKick_DisconnectsTheClientWithTheServersMessage()
    {
        await Session!.StepFramesAsync(30);

        Client!.DisconnectSimulator.SimulateKick("pharos says bye");

        bool disconnected = await StepUntilAsync(() => Client.Client.disconnectReason?.Contains("pharos says bye") == true, maxFrames: 300);
        Assert.True(disconnected, $"Disconnect reason: {Client.Client.disconnectReason ?? "none"}");
        Assert.Null(Server!.GetClientByPlayername(PlayerName));
    }

    [ClientServerScenario]
    public async Task SimulateNetworkError_BreaksTheConnectionOnBothSides()
    {
        await Session!.StepFramesAsync(30);

        Client!.DisconnectSimulator.SimulateNetworkError("cable cut");

        Assert.True(Session.IsLinkSevered);
        bool noticed = await StepUntilAsync(() => Client.Client.disconnectReason?.Contains("cable cut") == true, maxFrames: 600);
        Assert.True(noticed, $"Disconnect reason: {Client.Client.disconnectReason ?? "none"}");
        Assert.Null(Server!.GetClientByPlayername(PlayerName));
    }

    private BlockPos Ground(int offset)
    {
        var pos = Client!.Client.EntityPlayer.Pos;
        return new BlockPos((int)Math.Floor(pos.X) + 3 + offset, (int)Math.Floor(pos.Y), (int)Math.Floor(pos.Z), 0);
    }

    private async Task<int> FramesUntilBlockSeen(BlockPos pos)
    {
        var api = (ICoreServerAPI)Server!.Api;
        int granite = ServerHost!.RunOnGameThread(() =>
        {
            int id = api.World.GetBlock(new AssetLocation("game:rock-granite")).BlockId;
            api.World.BlockAccessor.SetBlock(id, pos);
            return id;
        });

        int frames = 0;
        while (frames < 600 && Client!.RunOnClientThread(() => Client.Client.World.BlockAccessor.GetBlock(pos).BlockId) != granite)
        {
            await Session!.StepAsync();
            frames++;
        }

        return frames;
    }
}
