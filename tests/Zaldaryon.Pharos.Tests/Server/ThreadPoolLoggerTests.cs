using Vintagestory.API.Common;
using Xunit;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The game's thread pool has a logger after the host boots. Without one, a task that throws crashes the test host.</summary>
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class ThreadPoolLoggerTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task TheGameThreadPoolHasALogger()
    {
        await Task.Yield();
        Assert.NotNull(TyronThreadPool.Inst.Logger);
    }
}
