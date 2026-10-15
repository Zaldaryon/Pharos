using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>Restarting a server on its save: what the save keeps, and what a new server starts from.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public class ServerRestartTests
{
    private static readonly BlockPos Probe = new(5, 60, 5);

    [Fact]
    public async Task GracefulRestart_KeepsAChangeMadeBeforeIt()
    {
        using ServerSandbox sandbox = new();
        using EmbeddedServerHost first = EmbeddedServerHost.Boot(sandbox, Options());
        await first.EnsureChunksLoadedAsync(Probe, Probe);
        SetBlock(first, Probe, "game:rock-granite");

        EmbeddedServerHost second = await first.RestartAsync(RestartMode.Graceful);
        try
        {
            Assert.False(first.IsRunning, "The old server should be stopped after the restart.");
            Assert.True(second.IsRunning, "The new server should be running.");
            await second.EnsureChunksLoadedAsync(Probe, Probe);
            Assert.Equal("game:rock-granite", BlockCode(second, Probe));
        }
        finally
        {
            second.Dispose();
        }
    }

    private static ServerWorldOptions Options() => new()
    {
        WorldName = "pharos-restart",
        WorldType = "superflat",
        PlayStyle = "creativebuilding",
        Seed = "4242",
    };

    private static void SetBlock(EmbeddedServerHost host, BlockPos pos, string code) => host.RunOnGameThread(() =>
    {
        ICoreServerAPI api = (ICoreServerAPI)host.Server.Api;
        Block block = api.World.GetBlock(new AssetLocation(code)) ?? throw new InvalidOperationException($"The game has no block {code}.");
        api.World.BlockAccessor.SetBlock(block.BlockId, pos);
    });

    private static string BlockCode(EmbeddedServerHost host, BlockPos pos) => host.RunOnGameThread(() =>
        ((ICoreServerAPI)host.Server.Api).World.BlockAccessor.GetBlock(pos).Code.ToString());
}
