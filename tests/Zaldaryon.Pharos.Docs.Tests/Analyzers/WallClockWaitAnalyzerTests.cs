using System.Threading.Tasks;
using Xunit;
using Zaldaryon.Pharos.Analyzers;

namespace Zaldaryon.Pharos.Docs.Tests.Analyzers;

public class WallClockWaitAnalyzerTests
{
    private readonly WallClockWaitAnalyzer _analyzer = new();

    [Fact]
    public async Task TaskDelayInScenarioMethod_ReportsWarning()
    {
        const string source = """
        using System;
        using System.Threading.Tasks;

        public class ClientScenarioAttribute : Attribute { }

        public class MyScenario
        {
            [ClientScenario]
            public async Task TestMethod()
            {
                await Task.Delay(100);
            }
        }
        """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);
        var diag = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.WallClockWaitInScenario, diag.Id);
    }

    [Fact]
    public async Task ThreadSleepInScenarioMethod_ReportsWarning()
    {
        const string source = """
        using System;
        using System.Threading;

        public class ServerScenarioAttribute : Attribute { }

        public class MyScenario
        {
            [ServerScenario]
            public void TestMethod()
            {
                Thread.Sleep(100);
            }
        }
        """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);
        var diag = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.WallClockWaitInScenario, diag.Id);
    }

    [Fact]
    public async Task TaskDelayInNonScenarioMethod_DoesNotReportWarning()
    {
        const string source = """
        using System.Threading.Tasks;

        public class MyHelper
        {
            public async Task UtilityMethod()
            {
                await Task.Delay(100);
            }
        }
        """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);
        Assert.Empty(diagnostics);
    }
}
