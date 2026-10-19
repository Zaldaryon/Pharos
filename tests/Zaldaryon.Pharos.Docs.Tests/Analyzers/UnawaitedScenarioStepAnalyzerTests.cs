using System.Threading.Tasks;
using Xunit;
using Zaldaryon.Pharos.Analyzers;

namespace Zaldaryon.Pharos.Docs.Tests.Analyzers;

public class UnawaitedScenarioStepAnalyzerTests
{
    private readonly UnawaitedScenarioStepAnalyzer _analyzer = new();

    [Fact]
    public async Task UnawaitedStepAsync_ReportsWarning()
    {
        const string source = """
        using System.Threading.Tasks;

        public class MyScenario
        {
            public Task StepAsync() => Task.CompletedTask;

            public void TestMethod()
            {
                StepAsync();
            }
        }
        """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);
        var diag = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.UnawaitedScenarioStep, diag.Id);
    }

    [Fact]
    public async Task AwaitedStepAsync_DoesNotReportWarning()
    {
        const string source = """
        using System.Threading.Tasks;

        public class MyScenario
        {
            public Task StepAsync() => Task.CompletedTask;

            public async Task TestMethod()
            {
                await StepAsync();
            }
        }
        """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);
        Assert.Empty(diagnostics);
    }
}
