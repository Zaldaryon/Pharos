using Xunit;
using Vintagestory.Server;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>Exercises the attempt runner against a freshly booted embedded Vintage Story server.</summary>
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerWorld(seed: 253, playStyle: "creativebuilding", worldType: "superflat", Isolation = Isolation.Restart)]
public sealed class ScenarioRetryLiveTests : ServerScenarioBase
{
    private static int _attempts;
    private static EmbeddedServerHost? _firstServer;

    [ServerScenario(Attempts = 2)]
    public void ControlledFailure_RestartsTheEmbeddedServerAndPassesOnRetry()
    {
        Assert.NotNull(Server);
        Assert.NotNull(Api);

        int attempt = Interlocked.Increment(ref _attempts);

        if ((attempt & 1) == 1)
        {
            _firstServer = Server;
            Assert.Fail("Controlled first-attempt failure to exercise live scenario retry.");
        }

        Assert.Equal(0, attempt & 1);
        Assert.NotSame(_firstServer, Server);
    }
}
