using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.World;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.World;

[Collection("Sequential")]
[ServerWorld(seed: 251, playStyle: "creativebuilding", worldType: "superflat")]
public sealed class SceneLiveTests : ServerScenarioBase
{
    private static readonly IReadOnlyDictionary<char, string> Legend = new Dictionary<char, string>
    {
        ['#'] = "game:rock-granite",
        ['c'] = "game:rawclay-blue-none",
        ['~'] = "game:water-still-7",
        ['i'] = "game:lakeice",
    };

    [ServerScenario]
    public async Task BuildAndAssert_SupportSingleAndStackedLayers_AndRejectMissingChunks()
    {
        BlockPos origin = Spawn().AddCopy(320, 0, 320);
        Assert.Null(Host!.RunOnGameThread(() => Api!.World.BlockAccessor.GetChunkAtBlockPos(origin)));
        Assert.Throws<InvalidOperationException>(() => Scene.Build(Host, origin, Legend, "#"));

        await Host.EnsureChunksLoadedAsync(origin, origin.AddCopy(1, 1, 1));
        Scene.Build(Host, origin, Legend, "#.", "c#");
        Scene.Assert(Host, origin, Legend, "#.", "c#");

        BlockPos stacked = origin.AddCopy(4, 0, 0);
        Scene.Build(Host, stacked, Legend, ["##"], ["c."]);
        Scene.Assert(Host, stacked, Legend, ["##"], ["c."]);

        BlockPos liquid = origin.AddCopy(6, 0, 0);
        Host.RunOnGameThread(() => Api!.World.BlockAccessor.SetBlock(Api.World.GetBlock(new AssetLocation("game:rock-granite"))!.BlockId, liquid));
        Scene.Build(Host, liquid, Legend, "~");
        Scene.Assert(Host, liquid, Legend, "~");
        Assert.Equal("game:air", Host.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlock(liquid, BlockLayersAccess.Solid).Code.ToString()));
        Scene.Build(Host, liquid, Legend, ".");
        Scene.Assert(Host, liquid, Legend, ".");

        BlockPos ice = origin.AddCopy(8, 0, 0);
        Scene.Build(Host, ice, Legend, "i");
        Scene.Assert(Host, ice, Legend, "i");

        Host.RunOnGameThread(() => Api!.World.BlockAccessor.SetBlock(0, origin));
        PharosAssertException difference = Assert.Throws<PharosAssertException>(() => Scene.Assert(Host, origin, Legend, "#.", "c#"));
        Assert.Contains("(", difference.Message);
        Assert.Contains("expected solid game:rock-granite", difference.Message);
        Assert.Contains("found solid game:air", difference.Message);
    }

    [ServerScenario]
    public async Task AssertFailure_IsRetryableBySequenceWaitUntil()
    {
        BlockPos origin = Spawn().AddCopy(12, 0, 12);
        await Host!.EnsureChunksLoadedAsync(origin, origin);
        int ticks = 0;
        Host.RunOnGameThread(() => Api!.Event.RegisterGameTickListener(_ =>
        {
            if (Interlocked.Increment(ref ticks) == 2)
                Api.World.BlockAccessor.SetBlock(Api.World.GetBlock(new AssetLocation("game:rock-granite"))!.BlockId, origin);
        }, 0));

        await Sequence().WaitUntil(() => Scene.Assert(Host, origin, Legend, "#")).RunAsync(maxTicks: 5);

        Assert.Equal("game:rock-granite", Host.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlock(origin).Code.ToString()));
    }

    [ServerScenario]
    public async Task Build_ResolvesEveryBlockBeforeWritingAnyCell()
    {
        BlockPos origin = Spawn().AddCopy(8, 0, 8);
        await Host!.EnsureChunksLoadedAsync(origin, origin.AddCopy(1, 0, 0));
        int before = Host.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlock(origin).BlockId);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Scene.Build(
            Host, origin, new Dictionary<char, string> { ['#'] = "game:rock-granite", ['?'] = "game:no-such-block" }, "#?"));

        Assert.Contains("game:no-such-block", error.Message);
        Assert.Equal(before, Host.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlock(origin).BlockId));
    }

    private BlockPos Spawn() => Host!.RunOnGameThread(() => Server!.DefaultSpawnPosition.AsBlockPos);
}
