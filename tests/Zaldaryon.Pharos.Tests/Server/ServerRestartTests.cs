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
    private static readonly BlockPos Unsaved = new(6, 60, 5);

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

    [Fact]
    public async Task UnsavedChangeIsLostAfterACrash_SavedChangeIsKeptAfterAGracefulRestart()
    {
        using ServerSandbox sandbox = new();
        using EmbeddedServerHost first = EmbeddedServerHost.Boot(sandbox, Options());
        await first.EnsureChunksLoadedAsync(Probe, Probe);
        SetBlock(first, Probe, "game:rock-granite");

        // The graceful restart saves the first change. The second one is made after it, and only lives in memory.
        EmbeddedServerHost second = await first.RestartAsync(RestartMode.Graceful);
        EmbeddedServerHost? third = null;
        try
        {
            await second.EnsureChunksLoadedAsync(Probe, Probe);
            SetBlock(second, Unsaved, "game:rock-granite");

            third = await second.RestartAsync(RestartMode.Crash);
            Assert.False(second.IsRunning, "The crashed server should be stopped.");
            await third.EnsureChunksLoadedAsync(Probe, Probe);

            Assert.Equal("game:rock-granite", BlockCode(third, Probe));
            Assert.Equal("game:air", BlockCode(third, Unsaved));
        }
        finally
        {
            third?.Dispose();
            second.Dispose();
        }
    }

    [Fact]
    public async Task SaveWorldAsync_KeepsAChangeThroughACrash()
    {
        using ServerSandbox sandbox = new();
        using EmbeddedServerHost first = EmbeddedServerHost.Boot(sandbox, Options());
        await first.EnsureChunksLoadedAsync(Probe, Probe);
        SetBlock(first, Probe, "game:rock-granite");

        // The save is written in place, without a restart, so a crash afterwards keeps the change.
        await first.SaveWorldAsync();
        EmbeddedServerHost second = await first.RestartAsync(RestartMode.Crash);
        try
        {
            Assert.False(ServerThreadsAlive(first), "The crashed server should have no thread still running.");
            await second.EnsureChunksLoadedAsync(Probe, Probe);
            Assert.Equal("game:rock-granite", BlockCode(second, Probe));
        }
        finally
        {
            second.Dispose();
        }
    }

    // Whether any thread loop of the server is still alive, as the server's own shutdown reports it.
    private static bool ServerThreadsAlive(EmbeddedServerHost host)
    {
        System.Reflection.FieldInfo? loopsField = typeof(Vintagestory.Server.ServerMain).GetField("ServerThreadLoops", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (loopsField?.GetValue(host.Server) is not System.Collections.IEnumerable loops) return false;
        foreach (object? loop in loops)
        {
            if (loop is Vintagestory.Server.ServerThread { Alive: true }) return true;
        }

        return false;
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
