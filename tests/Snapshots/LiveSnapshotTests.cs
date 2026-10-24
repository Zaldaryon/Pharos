using VerifyTests;
using VerifyXunit;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Network;
using ProtoBuf;
using Zaldaryon.Pharos.UI;
using Zaldaryon.Pharos.Verify;
using Zaldaryon.Pharos.World;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Snapshots;

[Collection("Sequential")]
[ServerWorld(seed: 312, playStyle: "creativebuilding", worldType: "superflat")]
[DataFiles("Fixtures/DataFiles/pharosconfig-server.json", To = "ModConfig/snapshot.json")]
public sealed class LiveRegionSnapshotTests : ServerScenarioBase
{
    public LiveRegionSnapshotTests() => PharosVerify.Initialize();

    [ServerScenario]
    public async Task Region_ContainsSceneLayers_Fluids_AndBlockEntityAttributes()
    {
        BlockPos origin = new(160, 100, 160);
        BlockPos max = origin.AddCopy(1, 1, 1);
        await Server.EnsureChunksLoadedAsync(origin, max);
        Dictionary<char, string> legend = new() { ['#'] = "game:rock-granite", ['c'] = "game:chest-east", ['~'] = "game:water-still-7" };
        Scene.Build(Server, origin, legend, ["#c", ".."], ["..", "~#"]);
        Scene.Assert(Server, origin, legend, ["#c", ".."], ["..", "~#"]);
        Server.RunOnGameThread(() =>
        {
            var container = (IBlockEntityContainer)Api.World.BlockAccessor.GetBlockEntity(origin.AddCopy(1, 0, 0));
            var stack = new ItemStack(Api.World.GetItem(new AssetLocation("game:grain-spelt")), 3);
            stack.Attributes.SetInt("snapshotCharge", 42);
            container.Inventory![0].Itemstack = stack;
        });
        string snapshot = Server.Snapshot.Region(origin, max);
        Assert.Equal(snapshot, Zaldaryon.Pharos.Verify.ServerSnapshot.Region(Server, origin, max));
        Assert.Contains("game:rock-granite | game:chest-east", snapshot);
        Assert.Contains("Fluid Y+1\ngame:air | game:air\ngame:water-still-7 | game:air", snapshot);
        Assert.Contains("snapshotCharge", snapshot);
        Assert.Contains("game:grain-spelt", snapshot);
        await Verifier.Verify(snapshot).UseDirectory("Baselines/live").UniqueForGameVersion().DisableDiff();
        Server.RunOnGameThread(() => ((IBlockEntityContainer)Api.World.BlockAccessor.GetBlockEntity(origin.AddCopy(1, 0, 0))).Inventory![0].Itemstack!.StackSize++);
        Assert.NotEqual(snapshot, Server.Snapshot.Region(origin, max));
    }

    [ServerScenario]
    public async Task Region_RejectsUnloaded_Invalid_Overflow_AndOtherDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Server.Snapshot.Region(new(0, 1, 0), new(-1, 1, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Server.Snapshot.Region(new(int.MinValue, 0, 0), new(int.MaxValue, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Server.Snapshot.Region(new(0, 1, 0, 1), new(1, 2, 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Server.Snapshot.Region(new(0, 1, 0), new(256, 256, 256)));
        BlockPos missing = new(16000, 100, 16000);
        Assert.Throws<InvalidOperationException>(() => Server.Snapshot.Region(missing, missing));
        await Server.EnsureChunksLoadedAsync(new(160, 100, 160), new(160, 100, 160));
        string fromWorker = await Task.Run(() => Server.Snapshot.Region(new(160, 100, 160), new(160, 100, 160)));
        Assert.Contains("Solid Y+0", fromWorker);
    }

    [ServerScenario]
    public async Task CalendarAndDataFilePorts_AreStableAcrossTicks()
    {
        var settings = new VerifySettings();
        settings.UniqueForGameVersion();
        settings.UseDirectory("Baselines/live");
        settings.ScrubDataFilePorts(DataFilePorts);
        var calendar = Server.RunOnGameThread(() => Api.World.Calendar);
        await Verifier.Verify(new { Calendar = calendar }, settings).DisableDiff();
    }
}

[Collection("Sequential")]
[ServerMods("TestMods/pharoslayoutmod", "TestMods/pharosnetmod")]
public sealed class LiveClientSnapshotTests : ClientServerScenarioBase
{
    public LiveClientSnapshotTests() => PharosVerify.Initialize();
    protected override HeadlessClientOptions ClientOptions => new() { BootMode = ClientBootMode.Engine, Width = 800, Height = 600 };

    [ClientServerScenario]
    public async Task GuiTooltipHudAndNetwork_AreCapturedFromTheRunningGame()
    {
        await Client.Commands.ExecuteSuccessAsync(".pharoslayout open ok");
        await Session.StepFramesAsync(2);
        GuiSnapshot gui = Client.Gui.Snapshot();
        DialogLayout dialog = Assert.Single(gui.Layouts, d => d.Dialog == "PharosLayoutDialog");
        Assert.Contains(dialog.Elements, e => e.Key == "first" && e.Text == "First");
        HudReadout hud = Client.Ui!.HudReadout();
        Assert.NotEmpty(hud.Dialogs);
        ItemTooltip tooltip = Client.Items.Tooltip(Client.Items.Stack("game:grain-spelt"));
        Assert.NotEmpty(tooltip.Title);
        Assert.NotEmpty(tooltip.Lines);
        long mark = Client.ModNetwork.Mark();
        await Task.Delay(0);
        await Client.Commands.ExecuteSuccessAsync(".pharosnet send 9");
        await Session.StepFramesAsync(60);
        ModMessage message = Client.ModNetwork.Messages("pharosnet").Last(m => m.Direction == ModMessageDirection.Sent);
        // Live HUD numbers depend on elapsed simulation. The static dialog and real tooltip/message
        // have reviewed baselines; the HUD's captured element tree is checked without freezing vitals.
        Assert.True(hud.Dialogs.Count > 0);
        await Verifier.Verify(new { Dialog = dialog, Tooltip = tooltip, Network = message })
            .UseDirectory("Baselines/live").UniqueForGameVersion().DisableDiff();
    }


}
