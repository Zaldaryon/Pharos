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
        BlockPos chestPos = await PlaceChestInFrontAsync();

        // A click: the client acts on it once. Each use the server gets toggles the chest.
        Assert.True(await Session.Blocks.UseAsync(chestPos), "The client never acted on the use click");

        bool serverOpened = await StepUntilAsync(ServerOpenedChest, maxFrames: 600);
        if (!serverOpened)
        {
            // The server explains a use it refuses, out of range for one, in its log.
            string logged = string.Join(Environment.NewLine, ServerHost!.Logs.Entries.Where(e => e.Message.Contains(PlayerName)).TakeLast(10));
            string serverSide = ServerHost.RunOnGameThread(() =>
                $"block {ServerApi.World.BlockAccessor.GetBlock(chestPos).Code}, block entity {ServerApi.World.BlockAccessor.GetBlockEntity(chestPos)?.GetType().Name ?? "none"}, " +
                $"player at {Server!.GetClientByPlayername(PlayerName).Entityplayer.Pos.XYZ}");
            string clientSide = Client!.RunOnClientThread(() => $"player at {Client.Client.EntityPlayer.Pos.XYZ}");
            Assert.Fail(
                $"Using the chest at {chestPos} never opened its inventory on the server. Server: {serverSide}. Client: {clientSide}. " +
                $"The server logged about the player:{Environment.NewLine}{logged}");
        }

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

    [ClientServerScenario]
    public async Task UseAsync_UsesTheBlockOnce()
    {
        BlockPos chestPos = await PlaceChestInFrontAsync();
        int uses = 0;
        BlockUsedDelegate onUse = (_, selection) =>
        {
            if (selection.Position.Equals(chestPos)) Interlocked.Increment(ref uses);
        };
        ServerHost!.RunOnGameThread(() => ServerApi.Event.DidUseBlock += onUse);
        try
        {
            Assert.True(await Session!.Blocks.UseAsync(chestPos), "The client never acted on the use click");
            await StepUntilAsync(() => Volatile.Read(ref uses) > 0, maxFrames: 600);
            await Session.StepFramesAsync(60);

            // However slow the machine, a click is one use: the client repeats a held use after a
            // quarter of a second of real time.
            Assert.Equal(1, Volatile.Read(ref uses));
        }
        finally
        {
            ServerHost.RunOnGameThread(() => ServerApi.Event.DidUseBlock -= onUse);
        }
    }

    /// <summary>Puts a chest two blocks in front of the player and waits until the client has it.</summary>
    private async Task<BlockPos> PlaceChestInFrontAsync()
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
        return chestPos;
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
