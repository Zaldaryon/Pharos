using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Metrics;
using PharosMetrics = Zaldaryon.Pharos.Metrics.Metrics;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The server profile measures systems, mods, block entities, entities, AI tasks and traffic.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerWorld(seed: 4242, Isolation = Isolation.Restart)]
[ServerMods("TestMods/pharosprofilemod")]
public class ServerProfileTests : ServerScenarioBase
{
    private const string Mod = "pharosprofilemod";

    [ServerScenario]
    public async Task Measure_TimesTheTicksAndTheSystems()
    {
        ServerProfile profile = await Server.Profile.MeasureAsync(20);

        Assert.Equal(20, profile.Ticks);
        Assert.Equal(20, profile.TickTimes.Count);
        Assert.True(profile.MeanMsPerTick > 0, "The tick duration was not measured.");
        Assert.NotEmpty(profile.Systems);
        Assert.Contains(profile.Systems.Keys, name => name.Contains("Entity", StringComparison.Ordinal));
        Assert.All(profile.Systems.Values, entry =>
        {
            Assert.True(entry.Calls > 0);
            Assert.True(entry.Ticks == 20);
            Assert.True(entry.TotalMs >= 0);
            Assert.True(entry.P95MsPerTick >= 0);
        });
    }

    [ServerScenario]
    public async Task Measure_AttributesListenersAndCallbacksToTheirMod()
    {
        ServerProfile profile = await Server.Profile.MeasureAsync(30);

        Assert.True(profile.Mods.TryGetValue(Mod, out ServerProfileEntry? mod), $"No entry for {Mod}; the profile has {string.Join(", ", profile.Mods.Keys)}.");
        Assert.True(mod.Calls > 0);
        Assert.True(mod.TotalMs > 0, "The mod's tick work measured as zero.");
        Assert.Contains(profile.Listeners.Keys, name => name.Contains("PharosProfileModSystem.OnTick", StringComparison.Ordinal));
        Assert.Contains(profile.Listeners.Keys, name => name.Contains("PharosProfileModSystem.OnDelayed", StringComparison.Ordinal));
    }

    [ServerScenario]
    public async Task Measure_AttributesAListenerToItsBlockEntityType()
    {
        BlockPos pos = Api.World.DefaultSpawnPosition.AsBlockPos.Add(3, 0, 3);
        SetBlock(pos, $"{Mod}:profileblock");
        Server.Ticks(5);

        ServerProfile profile = await Server.Profile.MeasureAsync(20);

        Assert.True(profile.BlockEntities.TryGetValue("PharosProfileBlockEntity", out ServerProfileEntry? quern),
            $"No block entity entry; the profile has {string.Join(", ", profile.BlockEntities.Keys)}.");
        Assert.Equal(1, quern.Instances);
        Assert.True(quern.Calls >= 20, $"The block entity listener ran {quern.Calls} times in 20 ticks.");
        Assert.True(quern.TotalMs > 0);
    }

    [ServerScenario]
    public async Task Measure_SplitsAnEntityByBehaviorAndAiTask()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("EntityWatcher");
        Vec3d near = player.Entity!.Pos.XYZ.AddCopy(2, 0, 0);
        Entity hen = SpawnEntity("game:chicken-hen", near);
        for (int i = 0; i < 50 && hen.State != EnumEntityState.Active; i++) Server.Tick();

        ServerProfile profile = await Server.Profile.MeasureAsync(20);

        string entity = Assert.Single(profile.Entities.Keys, code => code.Contains("chicken", StringComparison.Ordinal));
        Assert.True(profile.Entities[entity].Calls > 0);
        Assert.Contains(profile.EntityBehaviors.Keys, key => key.StartsWith(entity + "/", StringComparison.Ordinal));
        Assert.Contains(profile.AiTasks, task => task.Code is "wander" or "idle" or "meleeattack" or "fleeentity");
        Assert.All(profile.AiTasks, task =>
        {
            Assert.True(task.Calls > 0);
            Assert.True(task.TotalMs >= 0);
        });
    }

    [ServerScenario]
    public async Task Measure_ReportsTrafficPerPlayerAndChannel()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("ProfilePlayer");
        Server.Ticks(5);

        Task<ServerProfile> measuring = Server.Profile.MeasureAsync(40);
        await Task.Delay(100);
        player.Network.SendChat("hello during the profile");
        ServerProfile profile = await measuring;

        Assert.True(profile.Bandwidth.TotalBytesSent > 0, "The server sent nothing in two seconds of ticks.");
        Assert.True(profile.Bandwidth.TotalPacketsSent > 0);
        Assert.True(profile.Bandwidth.TotalBytesReceived > 0, "The server received nothing from the connected player.");
        Assert.True(profile.Bandwidth.Players.ContainsKey(player.Player!.PlayerName));

        ChannelTraffic channel = profile.Bandwidth.Channel("pharosprofile");
        Assert.True(channel.PacketsSent > 0,
            $"The mod's channel sent nothing while the player was connected; channels seen: {string.Join(", ", profile.Bandwidth.Channels.Select(pair => $"{pair.Key}={pair.Value.PacketsSent}"))}.");
        Assert.True(channel.BytesSent > 0);
        Assert.True(channel.BytesSentPerPlayer[player.Player.PlayerName] > 0);
        Assert.NotEmpty(channel.MessagesSent);
        Assert.NotEmpty(profile.Bandwidth.Players[player.Player.PlayerName].PacketsSentById);
        Assert.NotEmpty(profile.Bandwidth.Players[player.Player.PlayerName].PacketsReceivedById);
    }

    [ServerScenario]
    public async Task Publish_FeedsTheNamesIntoTheScenarioMetrics()
    {
        ServerProfile profile = await Server.Profile.MeasureAsync(10);
        PharosMetrics.Publish(profile);

        IReadOnlyDictionary<string, MetricSeries> metrics = PharosMetrics.Current!.Snapshot();
        Assert.Contains("server.tick.ms", metrics.Keys);
        Assert.Equal(10, metrics["server.tick.ms"].Samples.Count);
        string systemMetric = metrics.Keys.First(name =>
            name.StartsWith("server.tick.system.", StringComparison.Ordinal) &&
            name.EndsWith(".ms", StringComparison.Ordinal));
        Assert.Equal(10, metrics[systemMetric].Samples.Count);
        Assert.Contains(metrics.Keys, name => name.StartsWith($"server.tick.mod.{Mod}.", StringComparison.Ordinal));
        Assert.Contains(metrics.Keys, name => name.StartsWith("net.sent.", StringComparison.Ordinal));
        Assert.Equal(MetricClock.Tick, metrics["server.tick.ms"].Clock);
    }

    [ServerScenario]
    public async Task Measure_Twice_LeavesNoHooksBehind()
    {
        ServerProfile first = await Server.Profile.MeasureAsync(5);
        ServerProfile second = await Server.Profile.MeasureAsync(5);
        ServerProfile third = Server.Profile.Measure(5);

        Assert.Equal(5, first.Ticks);
        Assert.Equal(5, second.Ticks);
        Assert.Equal(5, third.Ticks);
        Assert.NotEmpty(third.Systems);
    }

    [ServerScenario]
    public async Task Measure_WithACancelledToken_LeavesNoMeasurementOpen()
    {
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Server.Profile.MeasureAsync(5, cancelled.Token));

        ServerProfile profile = await Server.Profile.MeasureAsync(5);
        Assert.Equal(5, profile.Ticks);
    }

    [ServerScenario]
    public void TickCostBelow_FailsOnAModThatWasNotMeasured()
    {
        ServerProfile profile = Server.Profile.Measure(5);

        PharosAssert.TickCostBelow(profile, Mod, 10_000);
        PharosAssertException missing = Assert.Throws<PharosAssertException>(() => PharosAssert.TickCostBelow(profile, "no-such-mod", 1));
        Assert.Contains("no-such-mod", missing.Message, StringComparison.Ordinal);
        Assert.Throws<PharosAssertException>(() => PharosAssert.TickCostBelow(profile, Mod, 0));
    }
}
