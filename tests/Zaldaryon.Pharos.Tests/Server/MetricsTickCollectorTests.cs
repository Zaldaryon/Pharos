using Xunit;
using Zaldaryon.Pharos.Metrics;
using Zaldaryon.Pharos.Performance;
using Zaldaryon.Pharos.XUnit;
using MetricsApi = Zaldaryon.Pharos.Metrics.Metrics;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The tick collector breaks a measured server tick down by server system.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public class MetricsTickCollectorTests : ServerScenarioBase
{
    [ServerScenario]
    public void TickCollector_TimesEachServerSystem()
    {
        ServerTickCollector collector = new(16);
        long listener = 0;
        Host.RunOnGameThread(() =>
        {
            collector.Open();
            listener = Host.Api.Event.RegisterGameTickListener(_ => { }, 1);
        });
        try
        {
            for (int i = 0; i < 5; i++) Host.Tick();
        }
        finally
        {
            IReadOnlyDictionary<string, IReadOnlyList<double>> systems = Host.RunOnGameThread(() =>
            {
                IReadOnlyDictionary<string, IReadOnlyList<double>> measured = collector.SystemTimes();
                ServerTickCollector.Close();
                Host.Api.Event.UnregisterGameTickListener(listener);
                return measured;
            });
            IReadOnlyDictionary<string, IReadOnlyList<double>> listeners = Host.RunOnGameThread(collector.ListenerTimes);

            Assert.NotEmpty(systems);
            Assert.All(systems.Values, samples => Assert.NotEmpty(samples));
            Assert.NotEmpty(listeners);
        }
    }

    [ServerScenario]
    [CollectMetrics(Metric.Tick | Metric.Memory | Metric.World)]
    public void AutomaticCollectors_RunWithTheScenario()
    {
        Assert.True(MetricsApi.IsActive);
        MetricsApi.Record("test.custom", 5, MetricUnit.Count, MetricDirection.LowerIsBetter);
        for (int i = 0; i < 5; i++) Host.Tick();
    }
}
