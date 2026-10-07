using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.World;

/// <summary>
/// <see cref="WorldIsolation.Rollback"/> with a joined engine-mode client: both tests change the
/// world, move the player and fill its inventory, and check that they start from a clean world on
/// the same client and server. Whichever runs second sees the rollback, on both sides.
/// </summary>
[Collection("Sequential")]
public class LiveRollbackTests : ClientServerScenarioBase
{
    private static HeadlessClient? s_previousClient;
    private static Vec3d? s_start;

    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    private ICoreServerAPI ServerApi => (ICoreServerAPI)Server!.Api;

    [ClientServerScenario]
    public Task FirstOrSecond_StartsFromTheJoinedWorld() => ChangeEverythingAsync();

    [ClientServerScenario]
    public Task SecondOrFirst_StartsFromTheJoinedWorld() => ChangeEverythingAsync();

    private async Task ChangeEverythingAsync()
    {
        if (s_previousClient != null)
        {
            Assert.Same(s_previousClient, Client);
        }

        s_previousClient = Client;
        await Session!.StepFramesAsync(30);

        Vec3d start = Client!.RunOnClientThread(() => Client.Client.EntityPlayer.Pos.XYZ.Clone());
        s_start ??= start;
        Assert.True(start.HorizontalSquareDistanceTo(s_start) < 0.25, $"The player starts at {start}, not back at {s_start}");

        BlockPos marker = new((int)Math.Floor(start.X) + 2, (int)Math.Floor(start.Y), (int)Math.Floor(start.Z), 0);
        int granite = ServerHost!.RunOnGameThread(() => ServerApi.World.GetBlock(new AssetLocation("game:rock-granite")).BlockId);
        Assert.NotEqual(granite, ServerHost.RunOnGameThread(() => ServerApi.World.BlockAccessor.GetBlock(marker).BlockId));
        Assert.NotEqual(granite, Client.RunOnClientThread(() => Client.Client.World.BlockAccessor.GetBlock(marker).BlockId));
        Assert.False(HoldsSticks(), "The player kept an item from the previous test");

        ServerHost.RunOnGameThread(() =>
        {
            ServerApi.World.BlockAccessor.SetBlock(granite, marker);
            IServerPlayer player = Server!.GetClientByPlayername(PlayerName).Player;
            player.InventoryManager.ActiveHotbarSlot.Itemstack = new ItemStack(ServerApi.World.GetItem(new AssetLocation("game:stick")), 5);
            player.InventoryManager.ActiveHotbarSlot.MarkDirty();
        });

        bool clientSawIt = await Session.StepUntilAsync(
            () => Client.RunOnClientThread(() => Client.Client.World.BlockAccessor.GetBlock(marker).BlockId == granite),
            maxFrames: 300);
        Assert.True(clientSawIt, "The client never saw the placed block");

        await Session.HoldAsync(PlayerAction.Forward, 30);
    }

    private bool HoldsSticks() =>
        ServerHost!.RunOnGameThread(() =>
            Server!.GetClientByPlayername(PlayerName).Player.InventoryManager.ActiveHotbarSlot.Itemstack?.Collectible.Code.Path == "stick");
}
