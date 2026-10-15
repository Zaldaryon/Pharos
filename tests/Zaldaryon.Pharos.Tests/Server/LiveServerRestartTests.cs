using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>An engine-mode client follows its server through a restart: a graceful one keeps every change, a crash keeps only the saved ones.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class LiveServerRestartTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 1280,
        Height = 720,
    };

    protected override string PlayerName => "RestartPlayer";

    [ClientServerScenario]
    public async Task AfterACrash_TheClientJoinsTheNewServer_WithoutTheUnsavedBlock()
    {
        BlockPos saved = ServerPlayer.Entity!.Pos.AsBlockPos.AddCopy(4, 0, 0);
        BlockPos unsaved = saved.AddCopy(1, 0, 0);
        await ServerHost!.EnsureChunksLoadedAsync(saved, unsaved);
        SetBlock(ServerHost, saved, "game:rock-granite");
        await ServerHost.SaveWorldAsync();
        SetBlock(ServerHost, unsaved, "game:rock-granite");
        EmbeddedServerHost crashed = ServerHost;

        await RestartServerAsync(RestartMode.Crash);

        Assert.NotSame(crashed, ServerHost);
        Assert.True(await StepUntilAsync(() => Client!.IsJoined, maxFrames: 600), "The client did not join the new server.");
        Assert.True(ServerHost!.RunOnGameThread(() => ServerHost.Server.GetClientByPlayername(PlayerName) != null), "The new server does not have the player.");
        await ServerHost.EnsureChunksLoadedAsync(saved, unsaved);
        Assert.Equal("game:rock-granite", BlockCode(ServerHost, saved));
        Assert.Equal("game:air", BlockCode(ServerHost, unsaved));
    }

    [ClientServerScenario]
    public async Task AfterAGracefulRestart_TheClientJoinsAndKeepsEveryChange()
    {
        BlockPos saved = ServerPlayer.Entity!.Pos.AsBlockPos.AddCopy(4, 0, 0);
        BlockPos unsaved = saved.AddCopy(1, 0, 0);
        await ServerHost!.EnsureChunksLoadedAsync(saved, unsaved);
        SetBlock(ServerHost, unsaved, "game:rock-granite");

        await RestartServerAsync(RestartMode.Graceful);

        Assert.True(await StepUntilAsync(() => Client!.IsJoined, maxFrames: 600), "The client did not join the new server.");
        await ServerHost!.EnsureChunksLoadedAsync(saved, unsaved);
        Assert.Equal("game:rock-granite", BlockCode(ServerHost, unsaved));
    }

    private static void SetBlock(EmbeddedServerHost host, BlockPos pos, string code) => host.RunOnGameThread(() =>
    {
        ICoreServerAPI api = (ICoreServerAPI)host.Server.Api;
        Block block = api.World.GetBlock(new AssetLocation(code)) ?? throw new InvalidOperationException($"The game has no block {code}.");
        api.World.BlockAccessor.SetBlock(block.BlockId, pos);
    });

    private static string BlockCode(EmbeddedServerHost host, BlockPos pos) => host.RunOnGameThread(() =>
        ((ICoreServerAPI)host.Server.Api).World.BlockAccessor.GetBlock(pos).Code.ToString());
}
