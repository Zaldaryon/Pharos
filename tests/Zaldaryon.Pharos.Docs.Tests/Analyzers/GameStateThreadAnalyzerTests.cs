using System.Threading.Tasks;
using Xunit;
using Zaldaryon.Pharos.Analyzers;

namespace Zaldaryon.Pharos.Docs.Tests.Analyzers;

public class GameStateThreadAnalyzerTests
{
    private readonly GameStateThreadAnalyzer _analyzer = new();

    [Fact]
    public async Task DirectClientWorldAccess_ReportsWarning()
    {
        const string source = """
        public class MyScenario
        {
            public void TestMethod()
            {
                var w = Client.Client.World;
            }
        }
        """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);
        var diag = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.GameStateAccessedOutsideGameThread, diag.Id);
    }

    [Fact]
    public async Task DirectServerWorldAccess_ReportsWarning()
    {
        const string source = """
        public class MyScenario
        {
            public void TestMethod()
            {
                var w = Server.Api.World;
            }
        }
        """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);
        var diag = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.GameStateAccessedOutsideGameThread, diag.Id);
    }

    [Fact]
    public async Task ClientWorldAccess_InsideRunOnClientThread_DoesNotReportWarning()
    {
        const string source = """
        public class MyScenario
        {
            public void TestMethod()
            {
                Client.RunOnClientThread(() => Client.Client.World);
            }
        }
        """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ServerWorldAccess_InsideRunOnGameThread_DoesNotReportWarning()
    {
        const string source = """
        public class MyScenario
        {
            public void TestMethod()
            {
                Server.RunOnGameThread(() => Server.Api.World);
            }
        }
        """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);
        Assert.Empty(diagnostics);
    }
}
