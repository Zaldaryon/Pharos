using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.World;

[Collection("Sequential")]
public class LiveBlockInteractionTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    private ICoreServerAPI ServerApi => (ICoreServerAPI)Server!.Api;

    [ClientServerScenario]
    public async Task HoldForward_WalksAndJumpLeavesTheGround()
    {
        await Session!.StepFramesAsync(60);
        Vec3d start = Client!.Client.EntityPlayer.Pos.XYZ.Clone();

        await Session.HoldAsync(PlayerAction.Forward, 60);
        Assert.True(Client.Client.EntityPlayer.Pos.XYZ.HorizontalSquareDistanceTo(start) > 1, "Holding forward did not walk the player");

        double groundY = Client.Client.EntityPlayer.Pos.Y;
        double highest = groundY;
        Client.Controls.Press(PlayerAction.Jump);
        for (int i = 0; i < 20; i++)
        {
            await Session.StepAsync();
            highest = Math.Max(highest, Client.Client.EntityPlayer.Pos.Y);
        }

        Client.Controls.Release(PlayerAction.Jump);
        Assert.True(highest > groundY + 0.5, $"Jumping rose only {highest - groundY:F2} blocks");
    }

    [ClientServerScenario]
    public async Task BreakAsync_RemovesTheBlockOnBothSides()
    {
        await Session!.StepFramesAsync(60);
        BlockPos ground = GroundInFront(2);
        Assert.NotEqual(0, ServerBlockId(ground));

        bool broken = await Session.Blocks.BreakAsync(ground);

        Assert.True(broken, "The aimed block was not broken");
        Assert.Equal(0, ServerBlockId(ground));
    }

    [ClientServerScenario]
    public async Task PlaceAsync_PutsTheBlockOnTheClickedFace()
    {
        await Session!.StepFramesAsync(60);
        BlockPos ground = GroundInFront(2);

        bool placed = await Session.Blocks.PlaceAsync(ground, BlockFacing.UP, "game:rock-granite");

        Assert.True(placed, "The block did not appear on both sides");
        Block onTop = ServerApi.World.BlockAccessor.GetBlock(ground.UpCopy());
        Assert.Equal("rock-granite", onTop.Code.Path);
    }

    [ClientServerScenario]
    public async Task UseAsync_OpensAChest()
    {
        await Session!.StepFramesAsync(60);
        BlockPos chestPos = GroundInFront(2).UpCopy();
        ServerHost!.RunOnGameThread(() =>
        {
            Block chest = ServerApi.World.GetBlock(new AssetLocation("game:chest-east"));
            ServerApi.World.BlockAccessor.SetBlock(chest.BlockId, chestPos);
        });
        // The server sends the block and its block entity in separate packets; wait for both.
        bool arrived = await StepUntilAsync(
            () => Client!.RunOnClientThread(() => Client.Client.World.BlockAccessor.GetBlockEntity(chestPos) != null),
            maxFrames: 300);
        Assert.True(arrived, "The chest's block entity never reached the client");

        // The client starts a use only on a frame where it sees the button down past its build
        // repeat delay, which a short press can miss on a slow machine. Using a chest toggles it,
        // so press again only while the server has not opened it.
        bool serverOpened = false;
        for (int attempt = 0; attempt < 3 && !serverOpened; attempt++)
        {
            await Session.Blocks.UseAsync(chestPos);
            serverOpened = await StepUntilAsync(ServerOpenedChest, maxFrames: 300);
        }
        Assert.True(serverOpened, "Using the chest never opened its inventory on the server");

        // The dialog opens when the server's reply arrives: a round trip through both sides'
        // network threads, which a busy machine can stretch over many frames.
        bool opened = await StepUntilAsync(
            () => Client!.RunOnClientThread(() => Client.Client.api.Gui.OpenedGuis.Any(g => g.GetType().Name.Contains("BlockEntityInventory"))),
            maxFrames: 600);

        if (!opened)
        {
            string dialogs = Client!.RunOnClientThread(() => string.Join(", ", Client.Client.api.Gui.OpenedGuis.Select(g => g.GetType().Name)));
            Assert.Fail($"The server opened the chest but the client never showed its dialog. Open dialogs: {dialogs}");
        }
    }

    private bool ServerOpenedChest() =>
        ServerHost!.RunOnGameThread(() =>
            Server!.GetClientByPlayername(PlayerName).Player.InventoryManager.OpenedInventories.Any(i => i.ClassName == "chest"));

    private BlockPos GroundInFront(int distance)
    {
        EntityPos pos = Client!.Client.EntityPlayer.Pos;
        return new BlockPos((int)Math.Floor(pos.X) + distance, (int)Math.Floor(pos.Y) - 1, (int)Math.Floor(pos.Z), 0);
    }

    private int ServerBlockId(BlockPos pos) =>
        ServerHost!.RunOnGameThread(() => ServerApi.World.BlockAccessor.GetBlock(pos).BlockId);
}
