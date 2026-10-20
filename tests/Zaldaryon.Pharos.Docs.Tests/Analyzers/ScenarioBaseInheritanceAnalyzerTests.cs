using System.Threading.Tasks;
using Xunit;
using Zaldaryon.Pharos.Analyzers;

namespace Zaldaryon.Pharos.Docs.Tests.Analyzers;

public class ScenarioBaseInheritanceAnalyzerTests
{
    private readonly ScenarioBaseInheritanceAnalyzer _analyzer = new();

    [Fact]
    public async Task ClientScenarioOnClassWithoutClientScenarioBase_ReportsWarning()
    {
        const string source = """
        using System;

        public class ClientScenarioAttribute : Attribute { }

        public class MyTestClass
        {
            [ClientScenario]
            public void TestMethod() { }
        }
        """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);
        var diag = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.ScenarioAttributeWithoutBaseClass, diag.Id);
    }

    [Fact]
    public async Task ClientScenarioOnClassWithClientScenarioBase_DoesNotReportWarning()
    {
        const string source = """
        using System;

        public class ClientScenarioAttribute : Attribute { }
        public class ClientScenarioBase { }

        public class MyTestClass : ClientScenarioBase
        {
            [ClientScenario]
            public void TestMethod() { }
        }
        """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ContentSweepOnClassWithoutServerScenarioBase_ReportsWarning()
    {
        const string source = """
        using System;

        public class ContentSweepAttribute : Attribute { }

        public class MyTestClass
        {
            [ContentSweep]
            public void Sweep() { }
        }
        """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.ScenarioAttributeWithoutBaseClass, diagnostic.Id);
    }

    [Fact]
    public async Task ContentSweepOnServerScenarioBase_DoesNotReportWarning()
    {
        const string source = """
        using System;

        public class ContentSweepAttribute : Attribute { }
        public class ServerScenarioBase { }

        public class MyTestClass : ServerScenarioBase
        {
            [ContentSweep]
            public void Sweep() { }
        }
        """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);
        Assert.Empty(diagnostics);
    }

}
