using Vintagestory.API.Common;
using Vintagestory.Server;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>Checks that an embedded server captures failures from asynchronous game callbacks.</summary>
[Collection("Sequential")]
[ServerWorld(seed: 4242, Isolation = WorldIsolation.Restart)]
public sealed class ThreadPoolLoggingTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task AsyncCallbackExceptionsAreLoggedByTheEmbeddedServer()
    {
        const string caller = "Pharos thread-pool logging test";
        const string exceptionMessage = "Expected callback failure for the logging test";

        Assert.Same(ServerMain.Logger, TyronThreadPool.Inst.Logger);
        TyronThreadPool.QueueTask(
            () => Task.FromException(new InvalidOperationException(exceptionMessage)),
            caller);

        bool logged = await Host!.TickUntilAsync(
            () => Host.Logs.Warnings.Any(entry => entry.Message.Contains(caller, StringComparison.Ordinal))
                && Host.Logs.Warnings.Any(entry => entry.Message.Contains(exceptionMessage, StringComparison.Ordinal)),
            maxTicks: 300);

        Assert.True(logged, "The embedded server did not capture the thread-pool callback warning and exception.");
    }
}
