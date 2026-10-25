using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.UI;
using Zaldaryon.Pharos.World;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.UI;

[Collection("Sequential")]
[ServerWorld(seed: 4242, playStyle: "surviveandbuild")]
[ServerMods("TestMods/pharoshudmod")]
public class HudInformationTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 1280,
        Height = 900,
    };

    private HudDriver Hud => Client!.Ui!.Hud;

    [ClientServerScenario]
    public async Task BlockInfo_AndFilteredRemappedHelp_ReadComposedModOutput_WithoutCallingTheModAgain()
    {
        await Session!.StepFramesAsync(30);
        BlockPos pos = Client!.RunOnClientThread(() => Client.Client.player.Entity.Pos.AsBlockPos.AddCopy(0, 1, 2));
        Scene.Build(Server!, pos, new Dictionary<char, string> { ['p'] = "pharoshudmod:probe" }, "p");
        bool originalInfo = Client.RunOnClientThread(() => ClientSettings.ShowBlockInfoHud);
        bool originalHelp = Client.RunOnClientThread(() => ClientSettings.ShowBlockInteractionHelp);
        var hotkey = Client.RunOnClientThread(() => Client.Client.api.Input.GetHotKeyByCode("sneak"));
        int originalKey = Client.RunOnClientThread(() => hotkey.CurrentMapping.KeyCode);
        try
        {
            Client.RunOnClientThread(() =>
            {
                ClientSettings.ShowBlockInfoHud = true;
                ClientSettings.ShowBlockInteractionHelp = true;
                hotkey.CurrentMapping.KeyCode = (int)Vintagestory.API.Client.GlKeys.K;
            });
            Assert.True(await Session.Blocks.AimAtAsync(pos));
            await Hud.WaitForAsync(h => h.BlockInfo.Title == "Pharos probe" && h.InteractionHelp.Entries.Count == 1, 120);
            var info = Hud.BlockInfo;
            Assert.True(info.Visible);
            Assert.Equal("pharoshudmod:probe", info.BlockCode);
            Assert.Equal(pos, info.BlockPosition);
            Assert.Null(info.Entity);
            Assert.Contains("Temperature: 1100", info.Lines);
            Assert.DoesNotContain(info.Lines, text => text.Contains('<'));
            var help = Assert.Single(Hud.InteractionHelp.Entries);
            Assert.Equal("sneak", help.HotkeyCode);
            Assert.Equal("Add fuel", help.Text);
            Assert.Contains(help.Hotkeys, key => key.Code == "sneak" && key.Label.Contains("K"));
            Assert.DoesNotContain(Hud.InteractionHelp.Entries, e => e.Text.Contains("Hidden"));
            ((HudHotkeySnapshot[])help.Hotkeys)[0] = new HudHotkeySnapshot("modified", "modified");
            Assert.Contains(Assert.Single(Hud.InteractionHelp.Entries).Hotkeys, key => key.Code == "sneak" && key.Label.Contains("K"));
            int before = ProbeCalls(pos);
            for (int i = 0; i < 5; i++)
            {
                Assert.Equal("Pharos probe", Hud.BlockInfo.Title);
                Assert.Single(Hud.InteractionHelp.Entries);
            }
            Assert.Equal(before, ProbeCalls(pos));
            Client.RunOnClientThread(() =>
            {
                var helpHud = Client.Client.api.Gui.LoadedGuis.OfType<HudElementInteractionHelp>().Single();
                HudDriver.RequiredField<DrawWorldInteractionUtil>(helpHud, "wiUtil").Composer.ReCompose();
            });
            await Session.StepFramesAsync(1);
            Assert.Single(Hud.InteractionHelp.Entries);
            Client.RunOnClientThread(() =>
            {
                Block probe = Client.Client.api.World.BlockAccessor.GetBlock(pos);
                probe.GetType().GetField("Temperature")!.SetValue(probe, 1200);
            });
            await Hud.WaitForAsync(h => h.BlockInfo.Lines.Contains("Temperature: 1200"), 120);
            Assert.Contains("Temperature: 1100", info.Lines);
            info.BlockPosition!.X += 100;
            Assert.Equal(pos, Hud.BlockInfo.BlockPosition);
            Client.RunOnClientThread(() => Client.TestPlayer.Camera.LookAt(Client.Client.player.Entity.Pos.XYZ.AddCopy(0, 20, -2)));
            await Hud.WaitForAsync(h => !h.BlockInfo.Visible && !h.InteractionHelp.Visible, 120);
            Assert.Empty(Hud.BlockInfo.Lines);
            Assert.Empty(Hud.InteractionHelp.Entries);
        }
        finally
        {
            Client.RunOnClientThread(() =>
            {
                hotkey.CurrentMapping.KeyCode = originalKey;
                ClientSettings.ShowBlockInfoHud = originalInfo;
                ClientSettings.ShowBlockInteractionHelp = originalHelp;
            });
        }
    }

    private int ProbeCalls(BlockPos pos) => Client!.RunOnClientThread(() =>
    {
        Block probe = Client.Client.api.World.BlockAccessor.GetBlock(pos);
        return (int)probe.GetType().GetField("ApplyCalls")!.GetValue(probe)!;
    });

    [ClientServerScenario]
    public async Task EntityInfo_IdentifiesTheActualSelectedEntity()
    {
        await Session!.StepFramesAsync(30);
        Vec3d pos = Client!.RunOnClientThread(() => Client.Client.player.Entity.Pos.XYZ.AddCopy(0, 0, 3));
        var entity = await Server!.Entities.SpawnAsync("game:armorstand", pos);
        bool original = Client.RunOnClientThread(() => ClientSettings.ShowBlockInfoHud);
        try
        {
            Client.RunOnClientThread(() => ClientSettings.ShowBlockInfoHud = true);
            Assert.True(await Session.Entities.AimAtAsync(entity));
            await Hud.WaitForAsync(h => h.BlockInfo.Entity?.EntityId == entity.EntityId, 120);
            var info = Hud.BlockInfo;
            Assert.True(info.Visible);
            Assert.Equal("game:armorstand", info.Entity!.Code);
            Assert.False(string.IsNullOrWhiteSpace(info.Title));
            Assert.Null(info.BlockCode);
            Assert.Null(info.BlockPosition);
        }
        finally
        {
            Client.RunOnClientThread(() => ClientSettings.ShowBlockInfoHud = original);
        }
    }

    [ClientServerScenario]
    public async Task ErrorsAndDiscoveries_StampFirstRenderedFrame_RepeatAndExpire()
    {
        await Session!.StepFramesAsync(2);
        long before = Client!.FrameController.TotalFrames;
        Client.RunOnClientThread(() => Client.Client.api.TriggerIngameError(this, "pharos-test", "Pharos HUD error"));
        await Hud.WaitForAsync(h => h.Errors.Any(e => e.Text == "Pharos HUD error"), 20);
        var first = Assert.Single(Hud.Errors, e => e.Text == "Pharos HUD error");
        Assert.InRange(first.Frame, before + 1, Client.FrameController.TotalFrames);
        await Session.StepFramesAsync(2);
        Assert.Equal(first.Frame, Assert.Single(Hud.Errors, e => e.Text == first.Text).Frame);
        Client.RunOnClientThread(() => Client.Client.api.TriggerIngameError(this, "pharos-test", first.Text));
        await Hud.WaitForAsync(h => h.Errors.Any(e => e.Text == first.Text && e.Frame > first.Frame), 20);
        await Hud.WaitForAsync(h => h.Errors.All(e => e.Text != first.Text), 400);
        Client.RunOnClientThread(() =>
        {
            Client.Client.api.TriggerIngameDiscovery(this, "pharos-test", "Pharos discovery");
            Client.Client.api.TriggerIngameDiscovery(this, "pharos-test", "Pharos discovery");
        });
        await Hud.WaitForAsync(h => h.Discoveries.Any(e => e.Text == "Pharos discovery"), 30);
        var discovery = Assert.Single(Hud.Discoveries, e => e.Text == "Pharos discovery");
        await Session.StepFramesAsync(2);
        Assert.Equal(discovery.Frame, Assert.Single(Hud.Discoveries, e => e.Text == discovery.Text).Frame);
        await Hud.WaitForAsync(h => h.Discoveries.Any(e => e.Text == discovery.Text && e.Frame > discovery.Frame), 800);
        await Hud.WaitForAsync(h => h.Discoveries.All(e => e.Text != discovery.Text), 500);
    }

    [ClientServerScenario]
    public async Task Reconnect_ObservesTheNewGame_WithoutRetainingThePreviousMessage()
    {
        Client!.RunOnClientThread(() => Client.Client.api.TriggerIngameError(this, "reconnect", "Before reconnect"));
        await Hud.WaitForAsync(h => h.Errors.Any(e => e.Text == "Before reconnect"), 30);
        var previous = Hud.Errors;
        await Session!.ReconnectAsync();
        await Session.StepFramesAsync(2);
        Assert.DoesNotContain(Hud.Errors, e => e.Text == "Before reconnect");
        Assert.True(Hud.Health.Max > 0);
        long before = Client.FrameController.TotalFrames;
        Client.RunOnClientThread(() => Client.Client.api.TriggerIngameError(this, "reconnect", "After reconnect"));
        await Hud.WaitForAsync(h => h.Errors.Any(e => e.Text == "After reconnect"), 30);
        Assert.InRange(Assert.Single(Hud.Errors, e => e.Text == "After reconnect").Frame, before + 1, Client.FrameController.TotalFrames);
        Assert.Contains(previous, e => e.Text == "Before reconnect");
    }

    [ClientServerScenario]
    public async Task BossBars_TrackRealBossHealth_AndDisappearWhenTheGameHidesThem()
    {
        await Session!.StepFramesAsync(30);
        Vec3d pos = Client!.RunOnClientThread(() => Client.Client.player.Entity.Pos.XYZ.AddCopy(0, 0, 12));
        pos.Y = Math.Floor(pos.Y);
        // Keep real physics enabled, but prevent unrelated fall damage after activating the boss.
        string[] floor = Enumerable.Repeat("sssss", 5).ToArray();
        string[] air = Enumerable.Repeat(".....", 5).ToArray();
        Scene.Build(Server!, new BlockPos((int)Math.Floor(pos.X) - 2, (int)pos.Y - 1, (int)Math.Floor(pos.Z) - 2),
            new Dictionary<char, string> { ['s'] = "game:rock-granite" }, floor, air, air, air);
        var boss = await Server!.Entities.SpawnAsync("game:eidolon-immobilized", pos);
        Assert.True(await Session.TryStepUntilAsync(() => Client.RunOnClientThread(() => Client.Client.GetEntityById(boss.EntityId) != null), 120));
        Assert.DoesNotContain(Hud.BossBars, b => b.EntityId == boss.EntityId);
        Server.Entities.StopTask(boss, "inactive");
        Server.RunOnGameThread(() =>
        {
            // Configure this real entity's regeneration so exact damage stays stable during client sync.
            boss.WatchedAttributes.SetFloat("regenSpeed", 0);
            boss.WatchedAttributes.SetBool("showHealthbar", true);
        });
        await Hud.WaitForAsync(h => h.BossBars.Any(b => b.EntityId == boss.EntityId && b.Fraction > 0.99f), 240);
        var before = Assert.Single(Hud.BossBars, b => b.EntityId == boss.EntityId);
        Assert.False(string.IsNullOrWhiteSpace(before.Name));
        Server.RunOnGameThread(() =>
        {
            Assert.Equal(500, boss.WatchedAttributes.GetTreeAttribute("health").GetFloat("currenthealth"));
            Assert.True(boss.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.Injury }, 100));
            Assert.Equal(400, boss.WatchedAttributes.GetTreeAttribute("health").GetFloat("currenthealth"));
        });
        // A recomposed bar can temporarily contain its default value; wait for the expected display, not any decrease.
        await Hud.WaitForAsync(h => h.BossBars.Any(b => b.EntityId == boss.EntityId && b.Fraction >= 0.7f && b.Fraction <= 0.9f), 120);
        var after = Assert.Single(Hud.BossBars, b => b.EntityId == boss.EntityId);
        Assert.InRange(after.Fraction, 0.7f, 0.9f);
        Client.RunOnClientThread(() =>
        {
            var bossHud = Assert.Single(Client.Client.api.Gui.LoadedGuis,
                h => h.IsOpened() && h.GetType().FullName == "Vintagestory.Client.NoObf.HudBosshealthBars" &&
                     HudDriver.RequiredField<Entity>(h, "TargetEntity").EntityId == boss.EntityId);
            var bar = HudDriver.RequiredField<GuiElementStatbar>(bossHud, "healthbar");
            Assert.Equal(400, bar.GetValue());
            Assert.Equal(500, HudDriver.RequiredField<float>(bar, "maxValue"));
            Assert.Equal(bar.GetValue() / HudDriver.RequiredField<float>(bar, "maxValue"), after.Fraction);
        });
        Server.RunOnGameThread(() => Assert.Equal(400, boss.WatchedAttributes.GetTreeAttribute("health").GetFloat("currenthealth")));
        Assert.True(before.Fraction > 0.99f);
        Server.RunOnGameThread(() => boss.WatchedAttributes.SetBool("showHealthbar", false));
        await Hud.WaitForAsync(h => h.BossBars.All(b => b.EntityId != boss.EntityId), 120);
    }
}
