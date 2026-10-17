using System.Threading.Tasks;
using Xunit;
using Zaldaryon.Pharos.Analyzers;

namespace Zaldaryon.Pharos.Docs.Tests.Analyzers;

public class RequireOptimumOnPlainFactAnalyzerTests
{
    private readonly RequireOptimumOnPlainFactAnalyzer _analyzer = new();

    [Fact]
    public async Task RequireOptimumWithPlainFact_ReportsWarning()
    {
        const string source = """
        using System;

        public class RequireOptimumAttribute : Attribute { }
        public class FactAttribute : Attribute { }

        public class MyTestClass
        {
            [RequireOptimum]
            [Fact]
            public void TestMethod() { }
        }
        """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);
        var diag = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.RequireOptimumOnPlainFact, diag.Id);
    }

    [Fact]
    public async Task RequireOptimumWithClientScenario_DoesNotReportWarning()
    {
        const string source = """
        using System;

        public class RequireOptimumAttribute : Attribute { }
        public class ClientScenarioAttribute : Attribute { }

        public class MyTestClass
        {
            [RequireOptimum]
            [ClientScenario]
            public void TestMethod() { }
        }
        """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);
        Assert.Empty(diagnostics);
    }
}
