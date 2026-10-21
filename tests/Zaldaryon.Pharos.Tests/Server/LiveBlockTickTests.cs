using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The random block ticks of the server and the snow that lies on a block.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public class LiveBlockTickTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task TheBlockTick_RunsTheBlockThatWantsIt()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = player.Entity!.Pos.AsBlockPos.DownCopy();
        Server.SetBlock(pos, "game:leaves-placed-oak");

        bool wanted = false;
        for (int seed = 0; seed < 100 && !wanted; seed++)
        {
            wanted = Server.BlockTicks.Tick(pos, seed);
        }

        Assert.True(wanted, "The leaves never wanted a tick.");
    }

    [ServerScenario]
    public async Task TheRegionTick_MeltsTheSnow()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = player.Entity!.Pos.AsBlockPos.DownCopy();
        Server.SetBlock(pos, "game:snowlayer-1");

        int changes = await Server.BlockTicks.TickRegionAsync(pos, pos, rounds: 50, seed: 7);

        Assert.True(changes > 0, "The snow did not melt.");
        Assert.Equal(0, Server.GetBlock(pos).Id);
    }

    [ServerScenario]
    public async Task TheRandomTicks_CanBePaused()
    {
        Server.BlockTicks.Pause();
        Assert.True(Server.BlockTicks.IsPaused);

        Server.BlockTicks.Resume();
        Assert.False(Server.BlockTicks.IsPaused);
    }

    [ServerScenario]
    public async Task TheSnowLevel_IsReadFromTheBlock()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = player.Entity!.Pos.AsBlockPos.DownCopy();
        Assert.True(Server.Snow.AllowsSnow(pos), "The ground does not take snow.");

        Server.SetBlock(pos, "game:snowlayer-3");

        Assert.Equal(3f, Server.Snow.LevelAt(pos), precision: 3);
    }
}
