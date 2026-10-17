using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Network;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Network;

/// <summary>A recording of a server block change, replayed into the engine-mode client after the change was undone.</summary>
[Collection("Sequential")]
public class ClientReplayTests : ClientServerScenarioBase
{
    [ClientServerScenario]
    public async Task ARecordedBlockChange_ReachesTheClientAgain()
    {
        await Session!.StepFramesAsync(30);
        BlockPos pos = Ground(0);
        ICoreServerAPI api = (ICoreServerAPI)ServerHost!.Server.Api;
        int granite = api.World.GetBlock(new AssetLocation("game:rock-granite")).BlockId;
        int air = api.World.GetBlock(new AssetLocation("game:air")).BlockId;

        Client!.PacketRecorder.Start();
        SetOnServer(pos, granite);
        await StepUntilClientSees(pos, granite);
        Client.PacketRecorder.Stop();
        RecordedSession recording = RecordedSession.From(Client.PacketRecorder);

        SetOnServer(pos, air);
        await StepUntilClientSees(pos, air);
        Assert.Equal(air, ClientBlock(pos));

        TrafficReplayResult result = await Client.Replay.ServerTrafficAsync(recording);

        Assert.True(result.Delivered > 0, "The replay delivered nothing");
        Assert.True(result.Frames > 0, "The replay stepped no frames");
        Assert.Equal(granite, ClientBlock(pos));
    }

    private BlockPos Ground(int offset)
    {
        Vec3d pos = Client!.RunOnClientThread(() => Client.Client.EntityPlayer.Pos.XYZ.Clone());
        return new BlockPos((int)Math.Floor(pos.X) + 3 + offset, (int)Math.Floor(pos.Y), (int)Math.Floor(pos.Z), 0);
    }

    private void SetOnServer(BlockPos pos, int blockId)
    {
        ICoreServerAPI api = (ICoreServerAPI)ServerHost!.Server.Api;
        ServerHost.RunOnGameThread(() => api.World.BlockAccessor.SetBlock(blockId, pos));
    }

    private int ClientBlock(BlockPos pos) => Client!.RunOnClientThread(() => Client.Client.World.BlockAccessor.GetBlock(pos).BlockId);

    private async Task StepUntilClientSees(BlockPos pos, int blockId)
    {
        int frames = 0;
        while (frames < 600 && ClientBlock(pos) != blockId)
        {
            await Session!.StepAsync();
            frames++;
        }

        Assert.Equal(blockId, ClientBlock(pos));
    }
}
