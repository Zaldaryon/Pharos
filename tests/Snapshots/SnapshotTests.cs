using System.Globalization;
using System.Text.Json;
using VerifyTests;
using VerifyXunit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Network;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.UI;
using Zaldaryon.Pharos.Verify;

namespace Zaldaryon.Pharos.Tests.Snapshots;

public sealed class SnapshotTests
{
#if PHAROS_VERIFY_V3
    private const string Baselines = "Baselines/v3";
#else
    private const string Baselines = "Baselines/v2";
#endif
    public SnapshotTests() => PharosVerify.Initialize();

    [Fact]
    public async Task Attributes_PreserveNestedTypesAndOrder()
    {
        var nested = new TreeAttribute();
        nested.SetInt("z", 123456);
        nested.SetBool("a", true);
        var tree = new TreeAttribute();
        tree["nested"] = nested;
        tree.SetString("code", "mod:crystal-123456");
        tree["same"] = nested; // Shared subtree, not a cycle.
        using var json = JsonDocument.Parse(await Received(tree));
        Assert.Equal(123456, json.RootElement.GetProperty("nested").GetProperty("z").GetInt32());
        Assert.True(json.RootElement.GetProperty("nested").GetProperty("a").GetBoolean());
        Assert.Equal("mod:crystal-123456", json.RootElement.GetProperty("code").GetString());
        Assert.Equal("a", json.RootElement.GetProperty("same").EnumerateObject().First().Name);
        Assert.Equal(new[] { "code", "nested", "same" }, json.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Attributes_ScrubIdentitiesButNotQuantitiesOrCodes()
    {
        TreeAttribute tree = new();
        tree.SetLong("entityId", 98123456789);
        tree.SetString("playerUID", "runtime-player-one");
        tree.SetInt("quantity", 123456);
        tree.SetString("code", "mod:123456");
        string text = await Received(tree);
        Assert.DoesNotContain("98123456789", text);
        Assert.DoesNotContain("runtime-player-one", text);
        Assert.Contains("123456", text);
        Assert.Contains("mod:123456", text);
    }

    [Fact]
    public async Task Floats_RoundNumbers_NotStrings_InEveryCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            using var json = JsonDocument.Parse(await Received(new { Number = 123.456789, Negative = -0.123456, Decimal = 1.234567m, Text = "123.456789", Count = 123456 }));
            Assert.Equal(123.4568, json.RootElement.GetProperty("Number").GetDouble());
            Assert.Equal(-0.1235, json.RootElement.GetProperty("Negative").GetDouble());
            Assert.Equal(1.2346m, json.RootElement.GetProperty("Decimal").GetDecimal());
            Assert.Equal("123.456789", json.RootElement.GetProperty("Text").GetString());
            Assert.Equal(123456, json.RootElement.GetProperty("Count").GetInt32());
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task Scrubbers_AreLocal_Explicit_AndLeaveCodesIntact()
    {
        string text = await Received(new { Port = 54321, Endpoint = "127.0.0.1:54321", Code = "mod:item-54321", Count = 123456, Player = "abc-player", Calendar = "May 1, 1386" },
            task => task.ScrubDataFilePorts(new Dictionary<string, int> { ["service"] = 54321 })
                .ScrubGameValue("abc-player", "Player").ScrubGameValue("May 1, 1386", "Calendar"));
        Assert.Contains("{Port:service}", text);
        Assert.Contains("mod:item-54321", text);
        Assert.Contains("123456", text);
        Assert.Contains("{Calendar}", text);
        Assert.DoesNotContain("abc-player", text);
        string next = await Received(new { Port = 54321, Player = "abc-player" });
        Assert.Contains("54321", next);
        Assert.Contains("abc-player", next);
    }

    [Fact]
    public void Scrubbers_RejectInvalidInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VerifySettings().ScrubDataFilePorts(new Dictionary<string, int> { ["bad"] = 0 }));
        Assert.Throws<ArgumentException>(() => new VerifySettings().ScrubGameValue("", "Player"));
    }

    [Fact]
    public async Task Initialize_IsIdempotent_AfterFirstVerification()
    {
        _ = await Received(new Vec3d(1, 2, 3));
        PharosVerify.Initialize();
        _ = await Received(new Vec3d(1, 2, 3));
    }

    [Fact]
    public async Task AttributeCycles_Fail_WithoutHidingData()
    {
        TreeAttribute tree = new();
        tree["self"] = tree;
        var error = await Assert.ThrowsAnyAsync<Exception>(() => Verifier.Verify(tree).DisableDiff().ToTask());
        Assert.Contains("cycle", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SemanticChanges_ChangeReceivedSnapshot()
    {
        ItemStack stack = Stack();
        string original = await Received(stack);
        stack.Attributes.SetInt("charge", 99);
        Assert.NotEqual(original, await Received(stack));
        var message = new ModMessage(1, 2, ModMessageDirection.Received, "demo", false, 0, "Sync", [1, 2], false);
        Assert.Equal(await Received(message), await Received(message with { Frame = 999, Sequence = 300 }));
        Assert.NotEqual(await Received(message), await Received(message with { Data = [1, 3] }));
        Entity entity = Entity();
        string before = await Received(entity);
        entity.WatchedAttributes.SetInt("health", 10);
        Assert.NotEqual(before, await Received(entity));
    }

    [Fact]
    public async Task ReceivedVerifiedWorkflow_DetectsMismatch_AndPreservesBaseline()
    {
        string directory = Path.Combine(Path.GetTempPath(), "pharos-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string verified = Path.Combine(directory, "workflow.verified.txt");
            File.WriteAllText(verified, "expected");
            await Verifier.Verify("expected").UseDirectory(directory).UseFileName("workflow").DisableRequireUniquePrefix().DisableDiff();
            await Assert.ThrowsAnyAsync<Exception>(() => Verifier.Verify("changed").UseDirectory(directory).UseFileName("workflow").DisableRequireUniquePrefix().DisableDiff().ToTask());
            Assert.Equal("expected", File.ReadAllText(verified));
            Assert.Equal("changed", File.ReadAllText(Path.Combine(directory, "workflow.received.txt")));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    public async Task VersionedNames_PreserveParameters(int value) =>
        await Verifier.Verify("versioned").UseDirectory(Baselines).UseParameters(value).UniqueForGameVersion().DisableDiff();

    [Fact]
    public async Task GameTypes_HaveReviewedSnapshots()
    {
        var layout = Layout();
        var gui = new GuiSnapshot(["Dialog"], ["HudStats"], true) { Layouts = [layout] };
        await Verifier.Verify(new
        {
            Tree = Stack().Attributes,
            Item = Stack(),
            Code = new AssetLocation("mod:crystal-blue"),
            Position = new BlockPos(123456, 12, -4, 0),
            Vector = new Vec3d(1.234567, -2.345678, 3),
            Entity = Entity(),
            Gui = gui,
            Dialog = layout,
            Tooltip = new ItemTooltip("Crystal", "<font color=\"red\">Charge</font><br>42 &amp; stable\n"),
            Network = new ModMessage(100, 999, ModMessageDirection.Received, "sync", false, 1, "CrystalSync", [8, 42], false),
            ServerNetwork = new ServerModMessage(90, 300, ModMessageDirection.Sent, "runtime-player", "Player", new Vec3d(1, 2, 3), "sync", false, 1, "CrystalSync", [8, 42]),
            Hud = new HudReadout([layout with { Dialog = "HudStats" }])
        }).UseDirectory(Baselines).UniqueForGameVersion().DisableDiff();
    }

    private static ItemStack Stack()
    {
        var stack = new ItemStack(new Item { Code = new AssetLocation("mod:crystal-blue") }, 3);
        stack.Attributes.SetInt("charge", 42);
        stack.Attributes.SetFloat("temperature", 12.345678f);
        stack.Attributes.SetString("ownerPlayerUID", "deliberate-custom-field");
        return stack;
    }

    private static Entity Entity()
    {
        var entity = new EntityItem { Code = new AssetLocation("mod:testentity"), EntityId = 123456789 };
        entity.Pos.SetPos(1.234567, 2, 3);
        entity.WatchedAttributes.SetInt("health", 20);
        entity.WatchedAttributes.SetString("playerUID", "runtime-player");
        return entity;
    }

    private static DialogLayout Layout() => new("Dialog", 800, 600, 1,
        [new ComposerBounds("main", 10, 20, 300, 150)],
        [new GuiElementInfo("Dialog", "main", "ok", "GuiElementTextButton", 12.345678, 25, 100, 30, "Accept", true)]);

    private static async Task<string> Received(object value, System.Func<SettingsTask, SettingsTask>? configure = null)
    {
        string directory = Path.Combine(Path.GetTempPath(), "pharos-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var task = Verifier.Verify(value).UseDirectory(directory).UseFileName("probe").UseStrictJson().DisableDiff();
            var error = await Assert.ThrowsAnyAsync<Exception>(() => (configure?.Invoke(task) ?? task).ToTask());
            Assert.Equal("VerifyException", error.GetType().Name);
            return File.ReadAllText(Path.Combine(directory, "probe.received.json"));
        }
        finally { Directory.Delete(directory, true); }
    }
}
